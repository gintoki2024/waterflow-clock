#import "ClockCountdown.h"
#include <math.h>

static NSInteger parseField(NSString *field, NSInteger maximum) {
    NSString *s = [field stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    if (!s.length || s.length>2) return -1;
    NSInteger result=0;
    for (NSUInteger i=0;i<s.length;i++) {
        unichar c=[s characterAtIndex:i];
        if (c<'0' || c>'9') return -1;
        result=result*10+c-'0';
    }
    return result<=maximum ? result : -1;
}

@implementation ClockCountdown
+ (instancetype)countdownNamed:(NSString *)name endingAt:(NSTimeInterval)deadline startingAt:(NSTimeInterval)now {
    if (!isfinite(now) || !isfinite(deadline) || deadline<=now) return nil;
    return [self fromRecord:@{@"mode":@"target", @"name":name ?: @"", @"deadline":@(deadline),
                              @"duration":@(ceil(deadline-now))}];
}
+ (instancetype)countdownNamed:(NSString *)name hours:(NSString *)hours minutes:(NSString *)minutes
                       seconds:(NSString *)seconds startingAt:(NSTimeInterval)now {
    NSInteger h=parseField(hours,99), m=parseField(minutes,59), s=parseField(seconds,59);
    if (h<0 || m<0 || s<0) return nil;
    NSInteger duration=h*3600+m*60+s;
    return [self fromRecord:@{@"name":name ?: @"", @"duration":@(duration), @"deadline":@(now+duration)}];
}
+ (instancetype)fromRecord:(id)record {
    if (![record isKindOfClass:NSDictionary.class] || ![record[@"name"] isKindOfClass:NSString.class] ||
        ![record[@"duration"] isKindOfClass:NSNumber.class] || ![record[@"deadline"] isKindOfClass:NSNumber.class]) return nil;
    NSString *name=[record[@"name"] stringByTrimmingCharactersInSet:NSCharacterSet.whitespaceAndNewlineCharacterSet];
    double duration=[record[@"duration"] doubleValue], deadline=[record[@"deadline"] doubleValue];
    id mode=record[@"mode"];
    BOOL target=[mode isEqual:@"target"];
    if (mode && !target && ![mode isEqual:@"duration"]) return nil;
    if (!name.length || !isfinite(duration) || duration<1 || duration>=(double)NSIntegerMax ||
        (!target && duration>359999) || floor(duration)!=duration ||
        !isfinite(deadline)) return nil;
    ClockCountdown *timer=[self new];
    timer->_name=[name copy]; timer->_duration=(NSInteger)duration; timer->_deadline=deadline; timer->_usesTargetTime=target;
    return timer;
}
- (NSDictionary *)record {
    return @{@"name":self.name, @"duration":@(self.duration), @"deadline":@(self.deadline),
             @"mode":self.usesTargetTime ? @"target" : @"duration"};
}
- (NSInteger)remainingAt:(NSTimeInterval)now {
    double remaining=MAX(0.,ceil(self.deadline-now));
    if (self.usesTargetTime) return remaining>=(double)NSIntegerMax ? NSIntegerMax : (NSInteger)remaining;
    return (NSInteger)MIN(self.duration,remaining);
}
+ (NSInteger)displayHoursForRemaining:(NSInteger)seconds {
    return seconds>=360000 ? (seconds/3600)%24 : seconds/3600;
}
- (NSString *)captionForRemaining:(NSInteger)seconds {
    if (seconds<360000) return self.name;
    return [NSString stringWithFormat:@"%ld 天 · %@",(long)(seconds/86400),self.name];
}
@end
