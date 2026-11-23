#import "ClockView.h"
#include <stdio.h>
#include <string.h>

@interface TimedClockView : ClockView
@property NSTimeInterval wall;
@property NSTimeInterval animation;
@end
@implementation TimedClockView
- (NSTimeInterval)wallTime { return self.wall; }
- (NSTimeInterval)animationTime { return self.animation; }
@end

static NSBitmapImageRep *Frame(ClockView *view) {
    NSSize size=view.bounds.size;
    NSBitmapImageRep *rep=[[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL
        pixelsWide:size.width pixelsHigh:size.height bitsPerSample:8 samplesPerPixel:4
        hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:32];
    [NSGraphicsContext saveGraphicsState];
    NSGraphicsContext.currentContext=[NSGraphicsContext graphicsContextWithBitmapImageRep:rep];
    [view drawRect:view.bounds];
    [NSGraphicsContext restoreGraphicsState];
    return rep;
}

static NSUInteger Changes(NSBitmapImageRep *a, NSBitmapImageRep *b, CGFloat from, CGFloat to) {
    NSUInteger changes=0;
    for (NSInteger y=0; y<a.pixelsHigh; y++) for (NSInteger x=a.pixelsWide*from; x<a.pixelsWide*to; x++) {
        NSUInteger left[4],right[4];
        [a getPixel:left atX:x y:y];
        [b getPixel:right atX:x y:y];
        if (memcmp(left,right,sizeof(left))!=0) changes++;
    }
    return changes;
}

static BOOL CheckTransition(NSInteger flow, BOOL countdown) {
    NSSize size=[ClockView sizeForScale:1];
    TimedClockView *v=[[TimedClockView alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
    v.theme=1; v.flow=flow; v.fontName=@"Helvetica";
    v.wall=1735734898;  // :58 to :59, irrespective of the local hour.
    v.animation=100;
    if (countdown)
        v.countdown=[ClockCountdown countdownNamed:@"Test" hours:@"0" minutes:@"0" seconds:@"18" startingAt:v.wall];
    NSBitmapImageRep *initial=Frame(v);
    v.wall+=1; v.animation+=1;
    Frame(v);
    v.animation+=.32;
    NSBitmapImageRep *moving=Frame(v);
    v.animation+=1.4;
    NSBitmapImageRep *settled=Frame(v);
    v.animation+=.5;
    return Changes(initial,moving,.80,1)>10 && Changes(moving,settled,.80,1)>10 &&
           Changes(initial,settled,.80,1)>10 && Changes(initial,moving,0,.75)==0 &&
           Changes(settled,Frame(v),0,1)==0;
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        for (int flow=0; flow<4; flow++) for (int theme=0; theme<4; theme++) {
            NSSize size=[ClockView sizeForScale:flow%3];
            ClockView *view=[[ClockView alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
            view.theme=theme; view.flow=flow; view.scale=flow%3; view.twelve=theme%2;
            view.fontName=theme%2 ? @"Helvetica" : @"随性手写（内置）";
            NSBitmapImageRep *rep=Frame(view);
            NSUInteger visible=0, clear=0;
            for (NSInteger y=0; y<rep.pixelsHigh; y+=2) for (NSInteger x=0;x<rep.pixelsWide;x+=2) {
                NSUInteger pixel[4]; [rep getPixel:pixel atX:x y:y];
                visible += pixel[3]>16; clear += pixel[3]==0;
            }
            if (visible<50 || clear<500) { fprintf(stderr,"Invalid pixels for theme %d flow %d\n",theme,flow); return 1; }
        }
        for (NSInteger flow=1; flow<=3; flow++) for (NSInteger counting=0; counting<=1; counting++) {
            if (!CheckTransition(flow,counting)) {
                fprintf(stderr,"Invalid illustrated transition: flow %ld countdown %ld\n",(long)flow,(long)counting);
                return 1;
            }
        }
        puts("16 theme/style combinations and six clock/countdown transitions passed");
    }
    return 0;
}
