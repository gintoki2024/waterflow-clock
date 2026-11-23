#import <Cocoa/Cocoa.h>
#import <ServiceManagement/ServiceManagement.h>
#import <QuartzCore/QuartzCore.h>
#include <math.h>
#import "ClockView.h"
#import "InkSampler.h"
#import "LoginItem.h"

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
    [self choices:@"流动效果" key:@"flow" labels:@[@"水波溪流",@"精密滚轮",@"静谧水影",@"纯净极简"]];
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
    _displayTimer = [NSTimer timerWithTimeInterval:1.0/40 repeats:YES block:^(NSTimer *t){ [self->_view setNeedsDisplay:YES]; }];
    [NSRunLoop.mainRunLoop addTimer:_displayTimer forMode:NSRunLoopCommonModes];
    _sampleTimer = [NSTimer scheduledTimerWithTimeInterval:0.8 repeats:YES block:^(NSTimer *t){
        if (self->_view.autoColor) [self->_sampler updateView:self->_view];
    }];
}
- (void)resetPosition {
    NSRect screen = NSScreen.screens.firstObject.visibleFrame;
    [_window setFrameOrigin:NSMakePoint(NSMaxX(screen)-NSWidth(_window.frame)-28,NSMaxY(screen)-NSHeight(_window.frame)-28)];
}
- (void)applySettings {
    _view.fontName = [_prefs stringForKey:@"font"]; _view.twelve = [_prefs boolForKey:@"twelve"];
    _view.flow = [_prefs integerForKey:@"flow"]; _view.scale = [_prefs integerForKey:@"scale"];
    _view.theme = [_prefs integerForKey:@"theme"]; _view.autoColor = [_prefs boolForKey:@"auto"];
    NSSize wanted = [self preferredSize];
    NSRect frame = _window.frame;
    if (!NSEqualSizes(frame.size,wanted))
        [_window setFrame:NSMakeRect(NSMinX(frame),NSMaxY(frame)-wanted.height,wanted.width,wanted.height) display:YES];
    _window.level = [_prefs boolForKey:@"top"] ? NSFloatingWindowLevel : NSNormalWindowLevel;
    _glass.hidden = _view.theme == 1; _window.hasShadow = !_glass.hidden;
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
    dialog.informativeText=@"输入名称和时长，最长 99 小时 59 分 59 秒。";
    [dialog addButtonWithTitle:@"开始"]; [dialog addButtonWithTitle:@"取消"];
    NSView *form=[[NSView alloc] initWithFrame:NSMakeRect(0,0,306,96)];
    NSTextField *name=[[NSTextField alloc] initWithFrame:NSMakeRect(0,66,306,24)];
    name.placeholderString=@"倒计时名称"; name.stringValue=_countdown.name ?: @"";
    [name setAccessibilityLabel:@"倒计时名称"]; [form addSubview:name];
    NSInteger duration=_countdown ? _countdown.duration : 300;
    NSArray *labels=@[@"小时",@"分钟",@"秒"];
    NSArray *defaults=@[@(duration/3600),@(duration/60%60),@(duration%60)];
    NSMutableArray<NSTextField *> *fields=[NSMutableArray array];
    NSView *previous=name;
    for (NSInteger i=0;i<3;i++) {
        NSTextField *field=[[NSTextField alloc] initWithFrame:NSMakeRect(i*104,7,92,24)];
        field.stringValue=[defaults[i] stringValue]; [field setAccessibilityLabel:labels[i]];
        NSTextField *label=[NSTextField labelWithString:labels[i]];
        label.frame=NSMakeRect(i*104,33,92,18);
        [form addSubview:label]; [form addSubview:field]; [fields addObject:field];
        previous.nextKeyView=field; previous=field;
    }
    previous.nextKeyView=name;
    dialog.accessoryView=form;
    dialog.window.initialFirstResponder=name;
    [NSApp activateIgnoringOtherApps:YES];
    while ([dialog runModal]==NSAlertFirstButtonReturn) {
        ClockCountdown *entry=[ClockCountdown countdownNamed:name.stringValue hours:fields[0].stringValue
            minutes:fields[1].stringValue seconds:fields[2].stringValue startingAt:NSDate.date.timeIntervalSince1970];
        if (entry) {
            [_prefs setObject:entry.record forKey:@"countdown"];
            [_prefs setBool:NO forKey:@"showClockInstead"];
            _countdown=entry; _view.countdown=entry;
            break;
        }
        dialog.informativeText=@"请填写名称。小时为 0–99，分钟和秒为 0–59，总时长须大于零。";
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
        else if ([@[@"theme",@"flow"] containsObject:key]) on = [_prefs integerForKey:key] == item.tag;
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
