#import <Cocoa/Cocoa.h>
#import <ServiceManagement/ServiceManagement.h>
#import <QuartzCore/QuartzCore.h>
#import "ClockView.h"
#import "InkSampler.h"

@interface ClockController : NSObject <NSApplicationDelegate, NSWindowDelegate, NSMenuDelegate>
@end
@implementation ClockController {
    NSWindow *_window;
    NSVisualEffectView *_glass;
    ClockView *_view;
    NSStatusItem *_status;
    NSUserDefaults *_prefs;
    InkSampler *_sampler;
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
- (void)applicationDidFinishLaunching:(NSNotification *)notification {
    _prefs = NSUserDefaults.standardUserDefaults;
    [_prefs registerDefaults:@{@"auto":@YES, @"font":@"随性手写（内置）", @"scale":@1}];
    _sampler = [InkSampler new];
    NSSize size = [ClockView sizeForScale:[_prefs integerForKey:@"scale"]];
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
    _glass.layer.cornerRadius = 18; _glass.layer.masksToBounds = YES;
    [root addSubview:_glass];
    _view = [[ClockView alloc] initWithFrame:root.bounds];
    _view.autoresizingMask = NSViewWidthSizable | NSViewHeightSizable; _view.wantsLayer = YES;
    [root addSubview:_view];
    _menu = [NSMenu new]; _menu.delegate = self;
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
    [_window setContentSize:[ClockView sizeForScale:_view.scale]];
    _window.level = [_prefs boolForKey:@"top"] ? NSFloatingWindowLevel : NSNormalWindowLevel;
    _glass.hidden = _view.theme == 1; _window.hasShadow = !_glass.hidden;
    _glass.material = _view.theme == 3 ? NSVisualEffectMaterialHUDWindow : NSVisualEffectMaterialPopover;
    _glass.appearance = [NSAppearance appearanceNamed:_view.theme==2 ? NSAppearanceNameAqua : NSAppearanceNameDarkAqua];
    [_sampler updateView:_view]; [_view setNeedsDisplay:YES];
}
- (void)windowDidMove:(NSNotification *)note {
    if (_window.visible) [_prefs setObject:NSStringFromPoint(_window.frame.origin) forKey:@"position"];
}
- (void)menuNeedsUpdate:(NSMenu *)menu {
    for (NSMenuItem *item in menu.itemArray) {
        NSString *key = item.representedObject;
        if (!key) continue;
        BOOL on = NO;
        if ([@[@"theme",@"flow",@"scale"] containsObject:key]) on = [_prefs integerForKey:key] == item.tag;
        else if ([@[@"top",@"auto",@"twelve"] containsObject:key]) on = [_prefs boolForKey:key];
        else if ([key isEqual:@"builtin"]) on = [[_prefs stringForKey:@"font"] isEqual:@"随性手写（内置）"];
        else if ([key isEqual:@"login"]) on = SMAppService.mainAppService.status == SMAppServiceStatusEnabled;
        item.state = on ? NSControlStateValueOn : NSControlStateValueOff;
    }
}
- (void)change:(NSMenuItem *)sender {
    NSString *key = sender.representedObject;
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
        if (service.status == SMAppServiceStatusEnabled) [service unregisterAndReturnError:&error];
        else [service registerAndReturnError:&error];
        if (error) [[NSAlert alertWithError:error] runModal];
        else if (service.status == SMAppServiceStatusRequiresApproval) [SMAppService openSystemSettingsLoginItems];
        return;
    }
    if ([key isEqual:@"builtin"]) [_prefs setObject:@"随性手写（内置）" forKey:@"font"];
    else if ([@[@"theme",@"scale",@"flow"] containsObject:key]) [_prefs setInteger:sender.tag forKey:key];
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
        ClockController *controller = [ClockController new];
        app.delegate = controller;
        [app run];
        (void)controller;
    }
    return 0;
}
