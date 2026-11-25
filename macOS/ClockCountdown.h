#import <Foundation/Foundation.h>

@interface ClockCountdown : NSObject
@property(nonatomic, readonly, copy) NSString *name;
@property(nonatomic, readonly) NSInteger duration;
@property(nonatomic, readonly) NSTimeInterval deadline;
@property(nonatomic, readonly) BOOL usesTargetTime;
@property(nonatomic, readonly) BOOL showSecondsOnly;
+ (NSTimeInterval)maxTargetInterval;
+ (instancetype)countdownNamed:(NSString *)name endingAt:(NSTimeInterval)deadline startingAt:(NSTimeInterval)now;
+ (instancetype)countdownNamed:(NSString *)name endingAt:(NSTimeInterval)deadline startingAt:(NSTimeInterval)now
               showSecondsOnly:(BOOL)secondsOnly;
+ (instancetype)countdownNamed:(NSString *)name hours:(NSString *)hours minutes:(NSString *)minutes
                       seconds:(NSString *)seconds startingAt:(NSTimeInterval)now;
+ (instancetype)countdownNamed:(NSString *)name hours:(NSString *)hours minutes:(NSString *)minutes
                       seconds:(NSString *)seconds startingAt:(NSTimeInterval)now
               showSecondsOnly:(BOOL)secondsOnly;
+ (instancetype)fromRecord:(id)record;
- (NSDictionary *)record;
- (NSInteger)remainingAt:(NSTimeInterval)now;
+ (NSInteger)displayHoursForRemaining:(NSInteger)seconds;
- (NSString *)captionForRemaining:(NSInteger)seconds;
@end
