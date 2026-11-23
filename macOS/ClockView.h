#import <Cocoa/Cocoa.h>

@interface ClockView : NSView
@property(nonatomic) BOOL twelve;
@property(nonatomic) BOOL autoColor;
@property(nonatomic) BOOL darkInk;
@property(nonatomic) NSInteger theme;
@property(nonatomic) NSInteger flow;
@property(nonatomic) NSInteger scale;
@property(nonatomic, copy) NSString *fontName;
+ (NSSize)sizeForScale:(NSInteger)scale;
+ (NSSize)sizeForWidth:(CGFloat)width;
@end

@interface ClockResizeHandle : NSView
@property(nonatomic, copy) void (^didFinishResize)(NSSize size);
+ (NSRect)frameByDragging:(NSSize)delta fromFrame:(NSRect)initial;
@end
