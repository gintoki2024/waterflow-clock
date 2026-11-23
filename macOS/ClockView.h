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
@end
