#import <Cocoa/Cocoa.h>
@class ClockView;

@interface InkSampler : NSObject
- (void)updateView:(ClockView *)view;
- (void)requestAccess;
@end
