#import <Cocoa/Cocoa.h>
@class ClockView;

@interface InkSampler : NSObject
- (void)updateView:(ClockView *)view;
- (void)applyBackgroundImage:(CGImageRef)image toView:(ClockView *)view;
- (void)requestAccess;
@end
