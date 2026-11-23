#import <Foundation/Foundation.h>

@interface ClockCountdown : NSObject
@property(nonatomic, readonly, copy) NSString *name;
@property(nonatomic, readonly) NSInteger duration;
@property(nonatomic, readonly) NSTimeInterval deadline;
@property(nonatomic, readonly) BOOL usesTargetTime;
+ (instancetype)countdownNamed:(NSString *)name endingAt:(NSTimeInterval)deadline startingAt:(NSTimeInterval)now;
+ (instancetype)countdownNamed:(NSString *)name hours:(NSString *)hours minutes:(NSString *)minutes
                       seconds:(NSString *)seconds startingAt:(NSTimeInterval)now;
+ (instancetype)fromRecord:(id)record;
- (NSDictionary *)record;
- (NSInteger)remainingAt:(NSTimeInterval)now;
+ (NSInteger)displayHoursForRemaining:(NSInteger)seconds;
- (NSString *)captionForRemaining:(NSInteger)seconds;
@end
