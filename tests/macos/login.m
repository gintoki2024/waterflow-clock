#import <Cocoa/Cocoa.h>
#import "LoginItem.h"
#include <stdio.h>

static SMAppServiceStatus waitForChange(SMAppServiceStatus oldStatus) {
    for (int i = 0; i < 40 && SMAppService.mainAppService.status == oldStatus; ++i)
        [[NSRunLoop currentRunLoop] runUntilDate:[NSDate dateWithTimeIntervalSinceNow:0.1]];
    return SMAppService.mainAppService.status;
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        SMAppServiceStatus initial = SMAppService.mainAppService.status;
        if (initial != SMAppServiceStatusNotRegistered) {
            NSLog(@"Unexpected initial login state: %ld", (long)initial);
            return 1;
        }
        NSError *error = nil;
        if (!ClockSetLoginEnabled(YES, &error)) { NSLog(@"Register failed: %@", error); return 2; }
        SMAppServiceStatus registered = waitForChange(initial);
        printf("macOS registration state: %s (%ld)\n",
               registered == SMAppServiceStatusEnabled ? "enabled" :
               registered == SMAppServiceStatusRequiresApproval ? "requires user approval" : "unexpected", (long)registered);
        if (!ClockSetLoginEnabled(NO, &error)) { NSLog(@"Unregister failed: %@", error); return 3; }
        SMAppServiceStatus final = waitForChange(registered);
        printf("macOS unregistration state: %ld\n", (long)final);
        return (registered == SMAppServiceStatusEnabled || registered == SMAppServiceStatusRequiresApproval)
            && final == SMAppServiceStatusNotRegistered ? 0 : 4;
    }
}
