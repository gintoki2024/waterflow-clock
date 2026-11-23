#import "ClockView.h"
#include <math.h>
#include <stdio.h>

static NSEvent *mouseEvent(NSEventType type, NSPoint point, NSWindow *window) {
    return [NSEvent mouseEventWithType:type location:point modifierFlags:0 timestamp:0
                          windowNumber:window.windowNumber context:nil eventNumber:1 clickCount:1 pressure:1];
}

int main(void) {
    @autoreleasepool {
        [NSApplication sharedApplication];
        NSSize normal = [ClockView sizeForScale:1];
        NSRect original = NSMakeRect(50,150,normal.width,normal.height);
        NSPanel *window = [[NSPanel alloc] initWithContentRect:original
            styleMask:NSWindowStyleMaskBorderless backing:NSBackingStoreBuffered defer:NO];
        window.opaque = NO;
        window.backgroundColor = NSColor.clearColor;
        ClockResizeHandle *handle = [[ClockResizeHandle alloc] initWithFrame:NSMakeRect(normal.width-27,1,26,26)];
        [window.contentView addSubview:handle];
        __block NSSize saved = NSZeroSize;
        handle.didFinishResize = ^(NSSize size){ saved = size; };
        NSPoint click = NSMakePoint(normal.width-10,12);
        if ([window.contentView hitTest:click] != handle) return 1;
        [handle mouseDown:mouseEvent(NSEventTypeLeftMouseDown,click,window)];
        NSPoint drag = NSMakePoint(click.x+83,click.y-43);
        [handle mouseDragged:mouseEvent(NSEventTypeLeftMouseDragged,drag,window)];
        [handle mouseUp:mouseEvent(NSEventTypeLeftMouseUp,drag,window)];
        if (saved.width < normal.width+80 || fabs(NSMaxY(window.frame)-NSMaxY(original))>1 ||
            fabs(saved.height-[ClockView sizeForWidth:saved.width].height)>1) return 2;
        for (int i=0;i<3;i++) {
            NSSize preset = [ClockView sizeForScale:i];
            if (!NSEqualSizes(preset,[ClockView sizeForWidth:preset.width])) return 3;
        }
        if ([ClockView sizeForWidth:-900].width!=160 || [ClockView sizeForWidth:9900].width!=2400)
            return 4;
        puts("Resize handle drag, transparent hit target, anchor, aspect ratio, presets and bounds passed");
    }
    return 0;
}
