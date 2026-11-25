#import <Cocoa/Cocoa.h>
#import "ClockCountdown.h"

@interface ClockView : NSView
@property(nonatomic) BOOL twelve;
@property(nonatomic) BOOL autoColor;
@property(nonatomic) BOOL darkInk;
@property(nonatomic) NSInteger theme;
@property(nonatomic) NSInteger flow;
@property(nonatomic) NSInteger scale;
@property(nonatomic, copy) NSString *fontName;
@property(nonatomic) NSInteger customBgMode; // 0=default, 1=solid color, 2=image
@property(nonatomic, strong) NSColor *customBgColor;
@property(nonatomic, copy) NSString *customBgImagePath;
@property(nonatomic, strong) ClockCountdown *countdown;
@property(nonatomic, readonly) BOOL isAnimating;
+ (NSSize)sizeForScale:(NSInteger)scale;
+ (NSSize)sizeForWidth:(CGFloat)width;
- (NSTimeInterval)wallTime;
- (NSTimeInterval)animationTime;
@end

@interface ClockResizeHandle : NSView
@property(nonatomic, copy) void (^didFinishResize)(NSSize size);
+ (NSRect)frameByDragging:(NSSize)delta fromFrame:(NSRect)initial;
@end
