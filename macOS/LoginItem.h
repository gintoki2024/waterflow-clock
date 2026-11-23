#import <ServiceManagement/ServiceManagement.h>

static inline BOOL ClockSetLoginEnabled(BOOL enabled, NSError **error) {
    SMAppService *item = SMAppService.mainAppService;
    return enabled ? [item registerAndReturnError:error] : [item unregisterAndReturnError:error];
}
