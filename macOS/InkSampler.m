#import "InkSampler.h"
#import "ClockView.h"
#import <CoreGraphics/CoreGraphics.h>
#import <ScreenCaptureKit/ScreenCaptureKit.h>
#include <dlfcn.h>

// Resolve the single-frame API at runtime so the same binary also runs on Ventura.
@protocol ClockScreenshotAPI
+ (void)captureImageWithFilter:(SCContentFilter *)filter configuration:(SCStreamConfiguration *)configuration
             completionHandler:(void (^)(CGImageRef, NSError *))completion;
@end

@implementation InkSampler {
    BOOL _busy;
    SCContentFilter *_filter;
    CGDirectDisplayID _display;
    NSDate *_filterDate;
}

- (void)requestAccess {
    if (!CGPreflightScreenCaptureAccess() && !CGRequestScreenCaptureAccess()) {
        NSURL *url = [NSURL URLWithString:@"x-apple.systempreferences:com.apple.preference.security?Privacy_ScreenCapture"];
        [[NSWorkspace sharedWorkspace] openURL:url];
    }
}

- (void)applyBackgroundImage:(CGImageRef)image toView:(ClockView *)view {
    if (!image || !view.autoColor) return;
    unsigned char samples[9] = {0};
    CGColorSpaceRef space = CGColorSpaceCreateDeviceGray();
    CGContextRef ctx = CGBitmapContextCreate(samples, 9, 1, 8, 9, space, kCGImageAlphaNone);
    CGColorSpaceRelease(space);
    if (!ctx) return;
    CGContextSetInterpolationQuality(ctx, kCGInterpolationHigh);
    CGContextDrawImage(ctx, CGRectMake(0, 0, 9, 1), image);
    CGContextRelease(ctx);
    for (int i = 1; i < 9; ++i) {
        int j = i; unsigned char value = samples[i];
        while (j && samples[j-1] > value) { samples[j] = samples[j-1]; --j; }
        samples[j] = value;
    }
    BOOL dark = samples[4] > (view.darkInk ? 107 : 132);
    if (dark != view.darkInk) {
        view.darkInk = dark;
        [view setNeedsDisplay:YES];
    }
}

- (void)updateView:(ClockView *)view {
    if (!view.autoColor || _busy || !view.window.screen) return;
    // Opaque-looking materials have a stable intended foreground. The floating
    // theme needs the actual underlying desktop/window pixels.
    if (view.theme != 1 || !CGPreflightScreenCaptureAccess()) {
        BOOL light = view.theme == 2;
        if (view.theme == 1)
            light = [[view.effectiveAppearance bestMatchFromAppearancesWithNames:
                @[NSAppearanceNameAqua, NSAppearanceNameDarkAqua]] isEqual:NSAppearanceNameAqua];
        if (light != view.darkInk) {
            view.darkInk = light;
            [view setNeedsDisplay:YES];
        }
        return;
    }
    NSWindow *window = view.window;
    NSScreen *screen = window.screen;
    NSRect band = NSInsetRect([window convertRectToScreen:[view convertRect:view.bounds toView:nil]],
                              NSWidth(view.bounds)*0.12, NSHeight(view.bounds)*0.46);
    band = NSIntersectionRect(band, screen.frame);
    if (NSIsEmptyRect(band)) return;
    CGRect local = CGRectMake(NSMinX(band)-NSMinX(screen.frame), NSMaxY(screen.frame)-NSMaxY(band),
                              NSWidth(band), NSHeight(band));
    CGDirectDisplayID display = [screen.deviceDescription[@"NSScreenNumber"] unsignedIntValue];
    Class<ClockScreenshotAPI> api = (Class)NSClassFromString(@"SCScreenshotManager");
    if (api && [((Class)api) respondsToSelector:@selector(captureImageWithFilter:configuration:completionHandler:)]) {
        if (!_filter || _display != display || [_filterDate timeIntervalSinceNow] < -30) {
            _busy = YES;
            [SCShareableContent getShareableContentExcludingDesktopWindows:NO onScreenWindowsOnly:YES
                completionHandler:^(SCShareableContent *content, NSError *error) {
                    dispatch_async(dispatch_get_main_queue(), ^{
                        self->_busy = NO;
                        self->_filter = nil;
                        for (SCDisplay *d in content.displays) if (d.displayID == display) {
                            NSMutableArray *excluded = [NSMutableArray array];
                            for (SCWindow *w in content.windows)
                                if (w.owningApplication.processID == NSProcessInfo.processInfo.processIdentifier)
                                    [excluded addObject:w];
                            self->_filter = [[SCContentFilter alloc] initWithDisplay:d excludingWindows:excluded];
                            self->_display = display;
                            self->_filterDate = [NSDate date];
                            break;
                        }
                    });
                }];
            return;
        }
        SCStreamConfiguration *config = [SCStreamConfiguration new];
        config.sourceRect = local; config.width = 90; config.height = 4; config.showsCursor = NO;
        _busy = YES;
        [api captureImageWithFilter:_filter configuration:config completionHandler:^(CGImageRef image, NSError *error) {
            if (image) CGImageRetain(image);
            dispatch_async(dispatch_get_main_queue(), ^{
                self->_busy = NO;
                if (image) {
                    if (view.window == window && view.theme == 1) [self applyBackgroundImage:image toView:view];
                    CGImageRelease(image);
                } else self->_filter = nil;
            });
        }];
    } else {
        CGRect bounds = CGDisplayBounds(display);
        CGRect rect = CGRectOffset(local, bounds.origin.x, bounds.origin.y);
        typedef CGImageRef (*LegacyCapture)(CGRect, CGWindowListOption, CGWindowID, CGWindowImageOption);
        LegacyCapture capture = (LegacyCapture)dlsym(RTLD_DEFAULT, "CGWindowListCreateImage");
        if (!capture) return;
        CGImageRef image = capture(rect, kCGWindowListOptionOnScreenBelowWindow,
            (CGWindowID)window.windowNumber, kCGWindowImageDefault);
        if (image) { [self applyBackgroundImage:image toView:view]; CGImageRelease(image); }
    }
}
@end
