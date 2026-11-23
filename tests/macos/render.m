#import "ClockView.h"
#include <stdio.h>
int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        for (int flow=0; flow<4; flow++) for (int theme=0; theme<4; theme++) {
            NSSize size=[ClockView sizeForScale:flow%3];
            ClockView *view=[[ClockView alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
            view.theme=theme; view.flow=flow; view.scale=flow%3; view.twelve=theme%2;
            view.fontName=theme%2 ? @"Helvetica" : @"随性手写（内置）";
            NSBitmapImageRep *rep=[[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL
                pixelsWide:size.width pixelsHigh:size.height bitsPerSample:8 samplesPerPixel:4
                hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:32];
            [NSGraphicsContext saveGraphicsState];
            NSGraphicsContext.currentContext=[NSGraphicsContext graphicsContextWithBitmapImageRep:rep];
            [view drawRect:view.bounds];
            [NSGraphicsContext restoreGraphicsState];
            NSUInteger visible=0, clear=0;
            for (NSInteger y=0; y<rep.pixelsHigh; y+=2) for (NSInteger x=0;x<rep.pixelsWide;x+=2) {
                NSUInteger pixel[4]; [rep getPixel:pixel atX:x y:y];
                visible += pixel[3]>16; clear += pixel[3]==0;
            }
            if (visible<50 || clear<500) { fprintf(stderr,"Invalid pixels for theme %d flow %d\n",theme,flow); return 1; }
        }
        puts("16 style combinations rendered with visible digits and transparent pixels");
    }
    return 0;
}
