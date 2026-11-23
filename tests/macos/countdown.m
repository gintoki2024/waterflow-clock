#import "ClockView.h"
#include <stdio.h>

static BOOL renderedBands(CGFloat width, NSInteger theme, NSInteger flow, BOOL expired) {
    NSSize size=[ClockView sizeForWidth:width];
    ClockView *view=[[ClockView alloc] initWithFrame:NSMakeRect(0,0,size.width,size.height)];
    view.theme=theme; view.flow=flow; view.twelve=YES;
    view.fontName=theme%2 ? @"Helvetica" : @"随性手写（内置）";
    NSTimeInterval start=NSDate.date.timeIntervalSince1970-(expired?800000:0);
    view.countdown=theme%2 ? [ClockCountdown countdownNamed:@"跨天目标 · 测试名称" endingAt:start+719999 startingAt:start] :
        [ClockCountdown countdownNamed:@"跨天倒计时 · 测试名称" hours:@"25" minutes:@"00" seconds:@"01" startingAt:start];
    NSBitmapImageRep *rep=[[NSBitmapImageRep alloc] initWithBitmapDataPlanes:NULL
        pixelsWide:ceil(size.width) pixelsHigh:ceil(size.height) bitsPerSample:8 samplesPerPixel:4
        hasAlpha:YES isPlanar:NO colorSpaceName:NSDeviceRGBColorSpace bytesPerRow:0 bitsPerPixel:32];
    [NSGraphicsContext saveGraphicsState];
    NSGraphicsContext.currentContext=[NSGraphicsContext graphicsContextWithBitmapImageRep:rep];
    [view drawRect:view.bounds];
    [NSGraphicsContext restoreGraphicsState];
    // Scan both outer bands. Bitmap scanline order can differ from flipped NSView coordinates.
    NSUInteger low=0, high=0, middle=0;
    for (NSInteger y=0;y<rep.pixelsHigh;y++) for (NSInteger x=0;x<rep.pixelsWide;x++) {
        NSUInteger p[4]; [rep getPixel:p atX:x y:y]; if (p[3]<80) continue;
        CGFloat fraction=(y+.5)/rep.pixelsHigh;
        if (fraction<.24) low++;
        else if (fraction>.76) high++;
        else if (fraction>.32 && fraction<.68) middle++;
    }
    return middle>40 && MAX(low,high)>20 && (!expired || MIN(low,high)>15);
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSTimeInterval now=1800000000.25;
        ClockCountdown *timer=[ClockCountdown countdownNamed:@"  项目收尾  " hours:@"99" minutes:@"59" seconds:@"59" startingAt:now];
        if (![timer.name isEqual:@"项目收尾"] || timer.duration!=359999 || [timer remainingAt:now]!=359999 ||
            [timer remainingAt:now+0.99]!=359999 || [timer remainingAt:now+1]!=359998 ||
            [timer remainingAt:timer.deadline-86400]!=86400 || [timer remainingAt:timer.deadline-.01]!=1 ||
            [timer remainingAt:timer.deadline]!=0 || [timer remainingAt:timer.deadline+600]!=0) return 1;
        NSData *saved=[NSPropertyListSerialization dataWithPropertyList:timer.record format:NSPropertyListBinaryFormat_v1_0
            options:0 error:NULL];
        ClockCountdown *restored=[ClockCountdown fromRecord:[NSPropertyListSerialization propertyListWithData:saved
            options:0 format:NULL error:NULL]];
        if (![restored.name isEqual:timer.name] || [restored remainingAt:now+90000]!=269999 ||
            [restored remainingAt:now+360000]!=0) return 2;
        NSMutableDictionary *legacy=[timer.record mutableCopy];
        [legacy removeObjectForKey:@"mode"];
        ClockCountdown *old=[ClockCountdown fromRecord:legacy];
        if (!old || old.usesTargetTime || [old remainingAt:now]!=359999) return 5;
        ClockCountdown *target=[ClockCountdown countdownNamed:@"交付" endingAt:now+400*86400+2 startingAt:now];
        ClockCountdown *loaded=[ClockCountdown fromRecord:target.record];
        if (!loaded.usesTargetTime || loaded.deadline!=target.deadline ||
            ![[loaded captionForRemaining:[loaded remainingAt:now]] isEqual:@"400 天 · 交付"] ||
            [loaded remainingAt:now-5]!=400*86400+7 ||
            [loaded remainingAt:loaded.deadline+.1]!=0 ||
            [ClockCountdown countdownNamed:@"A" endingAt:now startingAt:now] ||
            [ClockCountdown countdownNamed:@"A" endingAt:now-1 startingAt:now] ||
            [ClockCountdown countdownNamed:@" " endingAt:now+1 startingAt:now]) return 6;
        if (![[target captionForRemaining:360000] isEqual:@"4 天 · 交付"] ||
            [ClockCountdown displayHoursForRemaining:360000]!=4 ||
            ![[target captionForRemaining:359999] isEqual:@"交付"] ||
            [ClockCountdown displayHoursForRemaining:359999]!=99) return 7;
        for (NSArray *row in @[@[@" ",@"1",@"0",@"0"],@[@"A",@"0",@"0",@"0"],@[@"A",@"100",@"0",@"0"],
                             @[@"A",@"0",@"60",@"0"],@[@"A",@"0",@"0",@"60"],@[@"A",@"x",@"0",@"1"]])
            if ([ClockCountdown countdownNamed:row[0] hours:row[1] minutes:row[2] seconds:row[3] startingAt:now]) return 3;
        for (int theme=0;theme<4;theme++) for (int flow=0;flow<4;flow++) for (int expired=0;expired<=1;expired++) {
            if (!renderedBands(theme%2 ? 160 : 409,theme,flow,expired)) {
                fprintf(stderr,"Missing countdown text/digits in theme %d flow %d expired %d\n",theme,flow,expired);
                return 4;
            }
        }
        puts("Duration and target dates, legacy records, 100-hour caption boundary, subsecond rounding, restart, expiration and rendered labels passed");
    }
    return 0;
}
