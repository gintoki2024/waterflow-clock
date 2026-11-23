#import "ClockView.h"
#import <mach/mach.h>
#include <stdio.h>
#include <math.h>

@interface StressClock : ClockView
@property NSTimeInterval now;
@end
@implementation StressClock
- (NSTimeInterval)wallTime { return self.now; }
- (NSTimeInterval)animationTime { return self.now-1700000000; }
@end

static uint64_t physicalFootprint(void) {
    task_vm_info_data_t info;
    mach_msg_type_number_t count=TASK_VM_INFO_COUNT;
    kern_return_t status=task_info(mach_task_self(),TASK_VM_INFO,(task_info_t)&info,&count);
    return status==KERN_SUCCESS ? info.phys_footprint : 0;
}

static BOOL drawCycle(NSUInteger cycle) {
    __weak StressClock *released;
    @autoreleasepool {
        NSSize size=[ClockView sizeForWidth:(cycle%2 ? 240 : 360)];
        StressClock *view=[[StressClock alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
        released=view;
        NSBitmapImageRep *rep=[[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL pixelsWide:ceil(size.width)
            pixelsHigh:ceil(size.height) bitsPerSample:8 samplesPerPixel:4 hasAlpha:YES isPlanar:NO
            colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:32];
        [NSGraphicsContext saveGraphicsState];
        NSGraphicsContext.currentContext=[NSGraphicsContext graphicsContextWithBitmapImageRep:rep];
        for (NSInteger flow=0; flow<4; flow++) {
            view.flow=flow;
            view.theme=(cycle+flow)%4;
            view.now=1700000038;
            view.fontName=flow%2 ? @"Helvetica" : @"随性手写（内置）";
            view.countdown=cycle%3 ? [ClockCountdown countdownNamed:@"压力检查" endingAt:view.now+3 startingAt:view.now] : nil;
            for (int frame=0;frame<40;frame++) @autoreleasepool {
                view.now+=.1;
                [view drawRect:view.bounds];
            }
            if (view.countdown && view.isAnimating) {
                [NSGraphicsContext restoreGraphicsState];
                return NO;
            }
        }
        [NSGraphicsContext restoreGraphicsState];
    }
    return released==nil;
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        uint64_t baseline=0, end=0;
        for (NSUInteger i=0;i<36;i++) {
            if (!drawCycle(i)) { fprintf(stderr,"View remained retained or expired frame remained active\n"); return 1; }
            if (i==11) baseline=physicalFootprint();
        }
        end=physicalFootprint();
        if (!baseline || !end) { fprintf(stderr,"task_info footprint unavailable\n"); return 2; }
        printf("5760 frames, 36 released views; warmed footprint %.1f MiB, final %.1f MiB\n",
               baseline/1048576.,end/1048576.);
        if (end>baseline+96ull*1024*1024) { fprintf(stderr,"Post-warmup footprint increased by over 96 MiB\n"); return 3; }
    }
    return 0;
}
