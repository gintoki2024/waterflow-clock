#import "ClockView.h"
#import "InkSampler.h"
#include <stdio.h>

// Count actual images delivered by the capture path, without replacing it.
@interface CaptureProbe : InkSampler
@property(nonatomic) NSInteger delivered;
@end
@implementation CaptureProbe
- (void)applyBackgroundImage:(CGImageRef)image toView:(ClockView *)view {
    [super applyBackgroundImage:image toView:view];
    if (image && CGImageGetWidth(image)>0 && CGImageGetHeight(image)>0) self.delivered++;
}
@end

static BOOL checkRenderedInk(ClockView *view, BOOL dark) {
    NSBitmapImageRep *rep = [[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL
        pixelsWide:NSWidth(view.bounds) pixelsHigh:NSHeight(view.bounds) bitsPerSample:8 samplesPerPixel:4
        hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:32];
    [NSGraphicsContext saveGraphicsState];
    NSGraphicsContext.currentContext = [NSGraphicsContext graphicsContextWithBitmapImageRep:rep];
    [view drawRect:view.bounds];
    [NSGraphicsContext restoreGraphicsState];
    NSUInteger visible = 0, matching = 0;
    for (NSInteger y=0;y<rep.pixelsHigh;y+=2) for (NSInteger x=0;x<rep.pixelsWide;x+=2) {
        NSUInteger pixel[4];
        [rep getPixel:pixel atX:x y:y];
        if (pixel[3]<230) continue;
        visible++;
        CGFloat mean = (pixel[0]+pixel[1]+pixel[2])/3.;
        if (dark ? mean<95 : mean>165) matching++;
    }
    return view.darkInk==dark && visible>35 && matching>.95*visible;
}

static BOOL checkImage(InkSampler *sampler, ClockView *view, BOOL white) {
    CGColorSpaceRef space = CGColorSpaceCreateDeviceRGB();
    CGContextRef cg = CGBitmapContextCreate(NULL,90,4,8,0,space,kCGImageAlphaNoneSkipLast);
    CGColorSpaceRelease(space);
    if (!cg) return NO;
    CGContextSetRGBFillColor(cg,white,white,white,1);
    CGContextFillRect(cg,CGRectMake(0,0,90,4));
    CGImageRef sample = CGBitmapContextCreateImage(cg);
    CGContextRelease(cg);
    if (!sample) return NO;
    [sampler applyBackgroundImage:sample toView:view];
    CGImageRelease(sample);
    return checkRenderedInk(view,white);
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSScreen *screen = NSScreen.screens.firstObject;
        if (!screen) { fprintf(stderr,"No WindowServer display\n"); return 1; }
        NSRect bounds = NSMakeRect(NSMidX(screen.frame)-200,NSMidY(screen.frame)-100,400,208);
        NSPanel *panel = [[NSPanel alloc] initWithContentRect:bounds
            styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
        panel.opaque = NO;
        panel.backgroundColor = NSColor.clearColor;
        ClockView *view = [[ClockView alloc] initWithFrame:NSMakeRect(0,0,400,208)];
        view.theme=1; view.flow=3; view.autoColor=YES;
        panel.contentView=view;
        [panel orderFrontRegardless];
        InkSampler *sampler=[InkSampler new];
        if (!checkImage(sampler,view,YES) || !checkImage(sampler,view,NO)) {
            fprintf(stderr,"Digit bitmap failed white / black background contrast test\n");
            return 2;
        }
        puts("Controlled white and black CGImages produced dark and light rendered glyphs");
        if (!panel.screen) { fprintf(stderr,"Test panel is outside a display\n"); return 3; }
        CaptureProbe *probe=[CaptureProbe new];
        if (CGPreflightScreenCaptureAccess()) {
            NSDate *deadline=[NSDate dateWithTimeIntervalSinceNow:18];
            while (!probe.delivered && deadline.timeIntervalSinceNow>0) {
                [probe updateView:view];
                [NSRunLoop.currentRunLoop runUntilDate:[NSDate dateWithTimeIntervalSinceNow:.2]];
            }
            if (!probe.delivered || !checkRenderedInk(view,view.darkInk)) {
                fprintf(stderr,"Capture permission present but no usable screen sample or rendered ink\n");
                return 4;
            }
            puts("Screen capture authorized: production path delivered a real screen image and rendered its selected ink");
        } else {
            for (NSAppearanceName name in @[NSAppearanceNameAqua,NSAppearanceNameDarkAqua]) {
                BOOL expectDark = [name isEqual:NSAppearanceNameAqua];
                view.appearance=[NSAppearance appearanceNamed:name];
                view.darkInk=!expectDark;
                [probe updateView:view];
                if (probe.delivered || !checkRenderedInk(view,expectDark)) {
                    fprintf(stderr,"System appearance fallback failed\n"); return 5;
                }
            }
            puts("Screen capture permission unavailable: verified actual light/dark appearance fallback; live sampling untested");
        }
        [panel orderOut:nil];
    }
    return 0;
}
