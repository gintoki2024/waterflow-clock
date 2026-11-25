#import <Cocoa/Cocoa.h>
#import <ServiceManagement/ServiceManagement.h>
#import <QuartzCore/QuartzCore.h>
#include <math.h>
#import "ClockView.h"
#import "InkSampler.h"
#import "LoginItem.h"

@interface CountdownModeControl : NSSegmentedControl
@property(nonatomic,weak) NSView *durationFields;
@property(nonatomic,weak) NSView *targetFields;
- (void)showSelectedFields:(id)sender;
@end
@implementation CountdownModeControl
- (void)showSelectedFields:(id)sender {
    self.durationFields.hidden=self.selectedSegment==1;
    self.targetFields.hidden=self.selectedSegment!=1;
}
@end

@interface ClockController : NSObject <NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate>
@end
@implementation ClockController {
    NSWindow *_window;
    NSVisualEffectView *_glass;
    ClockView *_view;
    NSStatusItem *_status;
    NSUserDefaults *_prefs;
    InkSampler *_sampler;
    ClockCountdown *_countdown;
    NSMenu *_menu;
    NSTimer *_displayTimer;
    NSTimer *_sampleTimer;
    time_t _lastRenderedSecond;
}

static NSInteger NormalizeFlow(NSInteger raw) {
    if (raw == 4) return 1;
    if (raw == 5) return 2;
    if (raw == 6) return 3;
    return (raw >= 0 && raw <= 3) ? raw : 0;
}

- (NSMenuItem *)item:(NSString *)title key:(NSString *)key value:(NSInteger)value menu:(NSMenu *)menu {
    NSMenuItem *item = [menu addItemWithTitle:title action:@selector(change:) keyEquivalent:@""];
    item.target = self; item.representedObject = key; item.tag = value;
    return item;
}
- (void)choices:(NSString *)title key:(NSString *)key labels:(NSArray<NSString *> *)labels {
    NSMenu *sub = [[NSMenu alloc] initWithTitle:title]; sub.delegate = self;
    NSInteger i = 0;
    for (NSString *label in labels) [self item:label key:key value:i++ menu:sub];
    [_menu addItemWithTitle:title action:nil keyEquivalent:@""].submenu = sub;
}
- (NSSize)preferredSize {
    NSNumber *width = [_prefs objectForKey:@"customWidth"];
    return width ? [ClockView sizeForWidth:width.doubleValue] :
                   [ClockView sizeForScale:[_prefs integerForKey:@"scale"]];
}
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    _prefs = NSUserDefaults.standardUserDefaults;
    [_prefs registerDefaults:@{@"auto":@YES, @"font":@"随性手写（内置）", @"scale":@1}];
    _sampler = [InkSampler new];
    NSSize size = [self preferredSize];
    _window = [[NSPanel alloc] initWithContentRect:NSMakeRect(0,0,size.width,size.height)
        styleMask:NSWindowStyleMaskBorderless | NSWindowStyleMaskNonactivatingPanel
        backing:NSBackingStoreBuffered defer:NO];
    _window.delegate = self; _window.opaque = NO; _window.backgroundColor = NSColor.clearColor;
    _window.movableByWindowBackground = YES; _window.releasedWhenClosed = NO;
    ((NSPanel *)_window).hidesOnDeactivate = NO;
    _window.collectionBehavior = NSWindowCollectionBehaviorCanJoinAllSpaces | NSWindowCollectionBehaviorFullScreenAuxiliary;
    NSView *root = [[NSView alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
    _window.contentView = root;
    _glass = [[NSVisualEffectView alloc] initWithFrame:root.bounds];
    _glass.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable;
    _glass.blendingMode = NSVisualEffectBlendingModeBehindWindow;
    _glass.state = NSVisualEffectStateActive; _glass.wantsLayer = YES;
    _glass.layer.cornerRadius = size.width*18./(64*6.35); _glass.layer.masksToBounds = YES;
    [root addSubview:_glass];
    _view = [[ClockView alloc] initWithFrame:root.bounds];
    _view.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable; _view.wantsLayer = YES;
    [root addSubview:_view];
    _countdown=[ClockCountdown fromRecord:[_prefs objectForKey:@"countdown"]];
    if (![_prefs boolForKey:@"showClockInstead"]) _view.countdown=_countdown;
    ClockResizeHandle *handle = [[ClockResizeHandle alloc] initWithFrame:NSMakeRect(size.width-27,1,26,26)];
    handle.autoresizingMask = NSViewMinXMargin | NSViewMaxYMargin;
    handle.toolTip = @"拖动以等比例调整时钟大小";
    NSUserDefaults *settings = _prefs;
    handle.didFinishResize = ^(NSSize newSize) { [settings setDouble:newSize.width forKey:@"customWidth"]; };
    [root addSubview:handle];
    _menu = [NSMenu new]; _menu.delegate = self;
    [self item:@"设置倒计时…" key:@"countdown" value:0 menu:_menu];
    [self item:@"返回时钟" key:@"clock" value:0 menu:_menu];
    [_menu addItem:NSMenuItem.separatorItem];
    [self choices:@"外观" key:@"theme" labels:@[@"液态磨砂",@"纯净悬浮",@"极简白瓷",@"深海幽蓝"]];
    [self choices:@"数字特效" key:@"flow" labels:@[@"水波溪流",@"星尘消散",@"冰裂碎片",@"翻页记分牌"]];
    NSMenu *bgSub = [[NSMenu alloc] initWithTitle:@"自定义背景"];
    [self item:@"选择背景图片…" key:@"bgImage" value:0 menu:bgSub];
    [self item:@"选择背景颜色…" key:@"bgColor" value:0 menu:bgSub];
    [self item:@"恢复默认背景" key:@"bgReset" value:0 menu:bgSub];
    [_menu addItemWithTitle:@"自定义背景" action:nil keyEquivalent:@""].submenu = bgSub;
    [self choices:@"大小" key:@"scale" labels:@[@"小",@"标准",@"大"]];
    [self item:@"选择系统字体…" key:@"chooseFont" value:0 menu:_menu];
    [self item:@"内置手写字体" key:@"builtin" value:0 menu:_menu];
    [_menu addItem:NSMenuItem.separatorItem];
    [self item:@"12 小时制" key:@"twelve" value:0 menu:_menu];
    [self item:@"自动配色" key:@"auto" value:0 menu:_menu];
    [self item:@"允许读取窗口颜色…" key:@"capture" value:0 menu:_menu];
    [self item:@"始终置顶" key:@"top" value:0 menu:_menu];
    [self item:@"登录时启动" key:@"login" value:0 menu:_menu];
    [self item:@"移回主屏幕" key:@"reset" value:0 menu:_menu];
    [_menu addItem:NSMenuItem.separatorItem];
    NSMenuItem *quit = [_menu addItemWithTitle:@"退出水流时钟" action:@selector(terminate:) keyEquivalent:@"q"];
    quit.target = NSApp;
    _status = [NSStatusBar.systemStatusBar statusItemWithLength:NSSquareStatusItemLength];
    _status.button.image = [NSImage imageWithSystemSymbolName:@"clock" accessibilityDescription:@"水流时钟"];
    _status.menu = _menu; _view.menu = _menu;
    [self applySettings];
    NSString *position = [_prefs stringForKey:@"position"];
    if (position) [_window setFrameOrigin:NSPointFromString(position)];
    BOOL visible = NO;
    for (NSScreen *screen in NSScreen.screens)
        if (NSPointInRect(NSMakePoint(NSMidX(_window.frame),NSMidY(_window.frame)),screen.visibleFrame)) visible=YES;
    if (!position || !visible) [self resetPosition];
    [_window orderFrontRegardless];
    _lastRenderedSecond = -1;
    _displayTimer = [NSTimer timerWithTimeInterval:1.0/33.0 repeats:YES block:^(NSTimer *t){
        NSTimeInterval wall=self->_view.wallTime;
        time_t sec=self->_view.countdown ? [self->_view.countdown remainingAt:wall] : (time_t)floor(wall);
        if (self->_view.isAnimating || sec != self->_lastRenderedSecond) {
            self->_lastRenderedSecond = sec;
            [self->_view setNeedsDisplay:YES];
        }
    }];
    [NSRunLoop.mainRunLoop addTimer:_displayTimer forMode:NSRunLoopCommonModes];
    _sampleTimer = [NSTimer scheduledTimerWithTimeInterval:0.8 repeats:YES block:^(NSTimer *t){
        if (self->_view.autoColor) [self->_sampler updateView:self->_view];
    }];
}
- (void)applicationWillTerminate:(NSNotification *)note {
    [_displayTimer invalidate]; [_sampleTimer invalidate];
}
- (void)resetPosition {
    NSRect screen = NSScreen.screens.firstObject.visibleFrame;
    [_window setFrameOrigin:NSMakePoint(NSMaxX(screen)-NSWidth(_window.frame)-28,NSMaxY(screen)-NSHeight(_window.frame)-28)];
}
- (void)applySettings {
    _view.fontName = [_prefs stringForKey:@"font"]; _view.twelve = [_prefs boolForKey:@"twelve"];
    NSInteger flow = NormalizeFlow([_prefs integerForKey:@"flow"]);
    if (flow != [_prefs integerForKey:@"flow"]) [_prefs setInteger:flow forKey:@"flow"];
    _view.flow = flow; _view.scale = [_prefs integerForKey:@"scale"];
    _view.theme = [_prefs integerForKey:@"theme"]; _view.autoColor = [_prefs boolForKey:@"auto"];
    _view.customBgMode = [_prefs integerForKey:@"customBgMode"];
    _view.customBgImagePath = [_prefs stringForKey:@"customBgImage"];
    NSArray *rgba = [_prefs arrayForKey:@"customBgColor"];
    if (rgba.count == 4) {
        _view.customBgColor = [NSColor colorWithSRGBRed:[rgba[0] doubleValue] green:[rgba[1] doubleValue]
                                                   blue:[rgba[2] doubleValue] alpha:[rgba[3] doubleValue]];
    }
    _lastRenderedSecond = -1;
    NSSize wanted = [self preferredSize];
    NSRect frame = _window.frame;
    if (!NSEqualSizes(frame.size,wanted))
        [_window setFrame:NSMakeRect(NSMinX(frame),NSMaxY(frame)-wanted.height,wanted.width,wanted.height) display:YES];
    _window.level = [_prefs boolForKey:@"top"] ? NSFloatingWindowLevel : NSNormalWindowLevel;
    _glass.hidden = (_view.theme == 1 && _view.customBgMode == 0); _window.hasShadow = !_glass.hidden;
    _glass.material = _view.theme == 3 ? NSVisualEffectMaterialHUDWindow : NSVisualEffectMaterialPopover;
    _glass.appearance = [NSAppearance appearanceNamed:_view.theme==2 ? NSAppearanceNameAqua : NSAppearanceNameDarkAqua];
    [_sampler updateView:_view]; [_view setNeedsDisplay:YES];
}
- (void)windowDidMove:(NSNotification *)note {
    if (_window.visible) [_prefs setObject:NSStringFromPoint(_window.frame.origin) forKey:@"position"];
}
- (void)windowDidResize:(NSNotification *)note {
    _glass.layer.cornerRadius = NSWidth(_window.frame)*18./(64*6.35);
    [_view setNeedsDisplay:YES];
}
- (void)configureCountdown {
    NSAlert *dialog=[NSAlert new];
    dialog.messageText=@"设置倒计时";
    dialog.informativeText=@"填写名称，选择倒计时方式。";
    [dialog addButtonWithTitle:@"开始"]; [dialog addButtonWithTitle:@"取消"];
    NSView *form=[[NSView alloc] initWithFrame:NSMakeRect(0,0,342,168)];
    NSTextField *name=[[NSTextField alloc] initWithFrame:NSMakeRect(0,140,342,24)];
    name.placeholderString=@"倒计时名称"; name.stringValue=_countdown.name ?: @"";
    [name setAccessibilityLabel:@"倒计时名称"]; [form addSubview:name];
    CountdownModeControl *mode=[[CountdownModeControl alloc] initWithFrame:NSMakeRect(0,100,342,28)];
    mode.segmentCount=2; [mode setLabel:@"指定时长" forSegment:0]; [mode setLabel:@"到指定日期时间（10年内）" forSegment:1];
    mode.target=mode; mode.action=@selector(showSelectedFields:); mode.selectedSegment=_countdown.usesTargetTime?1:0;
    [form addSubview:mode]; name.nextKeyView=mode;
    NSView *durationFields=[[NSView alloc] initWithFrame:NSMakeRect(0,30,342,60)];
    NSView *targetFields=[[NSView alloc] initWithFrame:durationFields.frame];
    [form addSubview:durationFields]; [form addSubview:targetFields];
    mode.durationFields=durationFields; mode.targetFields=targetFields;
    NSButton *secondsCheck = [NSButton checkboxWithTitle:@"仅显示总秒数（纯秒倒计时）" target:nil action:nil];
    secondsCheck.frame = NSMakeRect(0, 2, 342, 22);
    secondsCheck.state = _countdown.showSecondsOnly ? NSControlStateValueOn : NSControlStateValueOff;
    [form addSubview:secondsCheck];
    NSInteger duration=_countdown && !_countdown.usesTargetTime ? _countdown.duration :
        [_prefs integerForKey:@"lastCountdownDuration"];
    if (duration<1 || duration>359999) duration=300;
    NSArray *labels=@[@"小时",@"分钟",@"秒"];
    NSArray *defaults=@[@(duration/3600),@(duration/60%60),@(duration%60)];
    NSMutableArray<NSTextField *> *fields=[NSMutableArray array];
    NSView *previous=mode;
    for (NSInteger i=0;i<3;i++) {
        NSTextField *field=[[NSTextField alloc] initWithFrame:NSMakeRect(i*104,7,92,24)];
        field.stringValue=[defaults[i] stringValue]; [field setAccessibilityLabel:labels[i]];
        NSTextField *label=[NSTextField labelWithString:labels[i]];
        label.frame=NSMakeRect(i*104,33,92,18);
        [durationFields addSubview:label]; [durationFields addSubview:field]; [fields addObject:field];
        previous.nextKeyView=field; previous=field;
    }
    NSTextField *targetLabel=[NSTextField labelWithString:@"本地日期和时间"];
    targetLabel.frame=NSMakeRect(0,33,342,18); [targetFields addSubview:targetLabel];
    NSDatePicker *target=[[NSDatePicker alloc] initWithFrame:NSMakeRect(0,5,342,26)];
    target.datePickerStyle=NSTextFieldAndStepperDatePickerStyle;
    target.datePickerElements=NSYearMonthDayDatePickerElementFlag | NSHourMinuteSecondDatePickerElementFlag;
    target.timeZone=NSTimeZone.localTimeZone;
    target.minDate=NSDate.date;
    target.maxDate=[NSDate dateWithTimeIntervalSinceNow:[ClockCountdown maxTargetInterval]];
    NSTimeInterval defaultDate=_countdown.usesTargetTime ? _countdown.deadline : ceil(NSDate.date.timeIntervalSince1970)+300;
    target.dateValue=[NSDate dateWithTimeIntervalSince1970:defaultDate];
    [target setAccessibilityLabel:@"本地目标日期、小时、分钟和秒"]; [targetFields addSubview:target];
    previous.nextKeyView=target; target.nextKeyView=name;
    [mode showSelectedFields:nil];
    dialog.accessoryView=form;
    dialog.window.initialFirstResponder=name;
    [NSApp activateIgnoringOtherApps:YES];
    while ([dialog runModal]==NSAlertFirstButtonReturn) {
        NSTimeInterval now=NSDate.date.timeIntervalSince1970;
        BOOL useTarget=mode.selectedSegment==1;
        BOOL secOnly=secondsCheck.state==NSControlStateValueOn;
        ClockCountdown *entry=useTarget
            ? [ClockCountdown countdownNamed:name.stringValue endingAt:floor(target.dateValue.timeIntervalSince1970)
                                  startingAt:now showSecondsOnly:secOnly]
            : [ClockCountdown countdownNamed:name.stringValue hours:fields[0].stringValue
                minutes:fields[1].stringValue seconds:fields[2].stringValue startingAt:now showSecondsOnly:secOnly];
        if (entry) {
            if (!_countdown.usesTargetTime && _countdown) [_prefs setInteger:_countdown.duration forKey:@"lastCountdownDuration"];
            if (!entry.usesTargetTime) [_prefs setInteger:entry.duration forKey:@"lastCountdownDuration"];
            [_prefs setObject:entry.record forKey:@"countdown"];
            [_prefs setBool:NO forKey:@"showClockInstead"];
            _countdown=entry; _view.countdown=entry;
            break;
        }
        dialog.informativeText=useTarget ? @"请填写名称，并选择未来 10 年内晚于当前时间的日期和时间。" :
            @"请填写名称。小时为 0–99，分钟和秒为 0–59，总时长须大于零。";
    }
}
- (void)menuNeedsUpdate:(NSMenu *)menu {
    for (NSMenuItem *item in menu.itemArray) {
        NSString *key = item.representedObject;
        if (!key) continue;
        if ([key isEqual:@"clock"]) {
            item.hidden=(_countdown==nil);
            item.title=_view.countdown ? @"返回时钟" : @"显示倒计时";
        }
        BOOL on = NO;
        if ([key isEqual:@"scale"]) on = fabs(_window.frame.size.width-[ClockView sizeForScale:item.tag].width)<1;
        else if ([key isEqual:@"theme"]) on = [_prefs integerForKey:key] == item.tag;
        else if ([key isEqual:@"flow"]) on = NormalizeFlow([_prefs integerForKey:key]) == item.tag;
        else if ([@[@"top",@"auto",@"twelve"] containsObject:key]) on = [_prefs boolForKey:key];
        else if ([key isEqual:@"builtin"]) on = [[_prefs stringForKey:@"font"] isEqual:@"随性手写（内置）"];
        else if ([key isEqual:@"login"]) on = SMAppService.mainAppService.status == SMAppServiceStatusEnabled;
        item.state = on ? NSControlStateValueOn : NSControlStateValueOff;
    }
}
- (void)change:(NSMenuItem *)sender {
    NSString *key = sender.representedObject;
    if ([key isEqual:@"countdown"]) { [self configureCountdown]; return; }
    if ([key isEqual:@"clock"]) {
        BOOL showClock=_view.countdown!=nil;
        [_prefs setBool:showClock forKey:@"showClockInstead"];
        _view.countdown=showClock ? nil : _countdown; return;
    }
    if ([key isEqual:@"reset"]) { [self resetPosition]; return; }
    if ([key isEqual:@"bgImage"]) {
        NSOpenPanel *panel = [NSOpenPanel openPanel];
        panel.canChooseFiles = YES; panel.canChooseDirectories = NO;
        [NSApp activateIgnoringOtherApps:YES];
        if ([panel runModal] == NSModalResponseOK && panel.URL.path.length) {
            [_prefs setInteger:2 forKey:@"customBgMode"];
            [_prefs setObject:panel.URL.path forKey:@"customBgImage"];
            [self applySettings];
        }
        return;
    }
    if ([key isEqual:@"bgColor"]) {
        NSColorPanel *cp = NSColorPanel.sharedColorPanel;
        cp.target = self; cp.action = @selector(changeBgColor:);
        [NSApp activateIgnoringOtherApps:YES];
        [cp orderFront:self];
        return;
    }
    if ([key isEqual:@"bgReset"]) {
        [_prefs setInteger:0 forKey:@"customBgMode"];
        [_prefs removeObjectForKey:@"customBgImage"];
        [self applySettings];
        return;
    }
    if ([key isEqual:@"capture"]) { [_sampler requestAccess]; return; }
    if ([key isEqual:@"chooseFont"]) {
        NSFontManager *manager = NSFontManager.sharedFontManager; manager.target = self;
        [manager setSelectedFont:[NSFont fontWithName:_view.fontName size:48] ?: [NSFont systemFontOfSize:48] isMultiple:NO];
        [NSApp activateIgnoringOtherApps:YES]; [manager orderFrontFontPanel:self]; return;
    }
    if ([key isEqual:@"login"]) {
        SMAppService *service = SMAppService.mainAppService; NSError *error = nil;
        if (service.status == SMAppServiceStatusRequiresApproval) { [SMAppService openSystemSettingsLoginItems]; return; }
        ClockSetLoginEnabled(service.status != SMAppServiceStatusEnabled, &error);
        if (error) [[NSAlert alertWithError:error] runModal];
        else if (service.status == SMAppServiceStatusRequiresApproval) [SMAppService openSystemSettingsLoginItems];
        return;
    }
    if ([key isEqual:@"builtin"]) [_prefs setObject:@"随性手写（内置）" forKey:@"font"];
    else if ([key isEqual:@"scale"]) {
        [_prefs removeObjectForKey:@"customWidth"];
        [_prefs setInteger:sender.tag forKey:key];
    }
    else if ([@[@"theme",@"flow"] containsObject:key]) [_prefs setInteger:sender.tag forKey:key];
    else [_prefs setBool:![_prefs boolForKey:key] forKey:key];
    [self applySettings];
}
- (void)changeBgColor:(NSColorPanel *)sender {
    NSColor *c = [sender.color colorUsingColorSpace:NSColorSpace.sRGBColorSpace];
    if (!c) return;
    [_prefs setInteger:1 forKey:@"customBgMode"];
    [_prefs setObject:@[@(c.redComponent), @(c.greenComponent), @(c.blueComponent), @(c.alphaComponent)] forKey:@"customBgColor"];
    [self applySettings];
}
- (void)changeFont:(NSFontManager *)sender {
    NSFont *font = [sender convertFont:sender.selectedFont ?: [NSFont systemFontOfSize:48]];
    if (font.fontName) { [_prefs setObject:font.fontName forKey:@"font"]; [self applySettings]; }
}
@end

int main(int argc, const char **argv) {
    @autoreleasepool {
        NSApplication *app = NSApplication.sharedApplication;
        [app setActivationPolicy:NSApplicationActivationPolicyAccessory];
        __attribute__((objc_precise_lifetime)) ClockController *controller = [ClockController new];
        app.delegate = controller;
        [app run];
        (void)controller;
    }
    return 0;
}
