#import "ClockView.h"
#import <CoreText/CoreText.h>
#include <math.h>
#include <time.h>

// Control points share the built-in Windows face's original 100 × 140 design grid.
// A zero point ends a stroke. A negative x begins a separate stroke.
static const CGPoint handwriting[10][16] = {
    {{58,14},{28,22},{14,58},{18,102},{40,127},{69,121},{85,86},{83,42},{65,16},{45,20}},
    {{32,37},{46,23},{56,14},{54,48},{50,92},{48,128}},
    {{21,38},{38,15},{67,16},{80,38},{68,66},{40,96},{19,122},{50,121},{84,115}},
    {{24,28},{50,13},{74,26},{70,50},{46,65},{74,78},{81,105},{58,127},{28,125},{18,111}},
    {{37,16},{28,49},{17,86},{49,84},{86,79},{-68,22},{65,62},{61,102},{59,130}},
    {{28,19},{25,44},{24,65},{52,59},{78,74},{80,103},{58,126},{29,126},{18,112},{-28,21},{54,17},{81,12}},
    {{68,14},{40,28},{20,64},{18,103},{40,127},{71,120},{82,93},{65,71},{36,73},{21,92}},
    {{16,23},{48,18},{82,16},{68,49},{51,89},{39,129}},
    {{54,16},{28,22},{24,45},{48,63},{78,82},{81,109},{54,128},{24,121},{19,94},{47,65},{73,44},{71,22},{50,16}},
    {{75,38},{60,15},{32,18},{18,44},{31,68},{59,66},{78,43},{71,79},{56,111},{42,130}}
};

static CGPathRef NewHandDigit(int digit) {
    CGMutablePathRef skeleton = CGPathCreateMutable();
    const CGPoint *points = handwriting[digit];
    for (int start = 0; start < 16 && points[start].x; ) {
        int end = start + 1;
        while (end < 16 && points[end].x > 0) end++;
        CGPoint path[16];
        for (int i = start; i < end; i++) path[i] = CGPointMake(fabs(points[i].x), -points[i].y);
        CGPathMoveToPoint(skeleton, NULL, path[start].x, path[start].y);
        for (int i = start; i + 1 < end; i++) {
            CGPoint a = path[MAX(start,i-1)], b = path[i], c = path[i+1], d = path[MIN(end-1,i+2)];
            CGPathAddCurveToPoint(skeleton, NULL, b.x+(c.x-a.x)/7, b.y+(c.y-a.y)/7,
                                  c.x-(d.x-b.x)/7, c.y-(d.y-b.y)/7, c.x,c.y);
        }
        start = end;
    }
    CGPathRef fill = CGPathCreateCopyByStrokingPath(skeleton, NULL, 11.8, kCGLineCapRound, kCGLineJoinRound, 2);
    CGPathRelease(skeleton);
    return fill;
}

static CGFloat ClockUnit(NSInteger scale) { return scale == 0 ? 48 : scale == 2 ? 83 : 64; }
static CGFloat NewTilt(void) { return ((NSInteger)arc4random_uniform(801)-400) * M_PI / 18000.; }

@implementation ClockView {
    NSArray *_paths;
    NSString *_cachedName;
    CGFloat _faceScale;
    CGRect _boundsForDigit[10];
    int _digits[8], _oldDigits[8];
    CGFloat _tilt[8], _oldTilt[8];
    double _start[8];
    BOOL _started;
}

+ (NSSize)sizeForScale:(NSInteger)scale {
    return [self sizeForWidth:ClockUnit(scale)*6.35];
}
+ (NSSize)sizeForWidth:(CGFloat)width {
    CGFloat w = isfinite(width) ? MAX(160,MIN(2400,width)) : ClockUnit(1)*6.35;
    return NSMakeSize(w,w*3.30/6.35);
}
- (BOOL)isOpaque { return NO; }
- (BOOL)mouseDownCanMoveWindow { return YES; }
- (void)setCountdown:(ClockCountdown *)countdown {
    if (_countdown == countdown) return;
    _countdown=countdown;
    _started=NO;
    self.toolTip=countdown.name;
    [self setNeedsDisplay:YES];
}

- (void)prepareGlyphs {
    NSString *name = self.fontName.length ? self.fontName : @"随性手写（内置）";
    if ([_cachedName isEqualToString:name]) return;
    BOOL hand = [name isEqualToString:@"随性手写（内置）"];
    NSFont *font = [NSFont fontWithName:name size:100];
    if (!font && !hand) font = [[NSFontManager sharedFontManager] fontWithFamily:name traits:0 weight:5 size:100];
    if (!font) font = [NSFont systemFontOfSize:100];
    CTFontRef face = CTFontCreateWithName((__bridge CFStringRef)font.fontName, 100, NULL);
    NSMutableArray *paths = [NSMutableArray arrayWithCapacity:10];
    CGFloat maxWidth = 1, maxHeight = 1;
    for (int d = 0; d < 10; d++) {
        UniChar ch = '0'+d;
        CGGlyph glyph;
        CGPathRef path = NULL;
        if (!hand && CTFontGetGlyphsForCharacters(face, &ch, &glyph, 1))
            path = CTFontCreatePathForGlyph(face, glyph, NULL);
        if (!path) path = NewHandDigit(d);
        _boundsForDigit[d] = CGPathGetPathBoundingBox(path);
        maxHeight = MAX(maxHeight, _boundsForDigit[d].size.height);
        maxWidth = MAX(maxWidth, _boundsForDigit[d].size.width);
        [paths addObject:CFBridgingRelease(path)];
    }
    CFRelease(face);
    _paths = paths;
    _cachedName = [name copy];
    _faceScale = MIN(.89/maxHeight, .64/maxWidth);
}

- (void)paintDigit:(int)d x:(CGFloat)x y:(CGFloat)y unit:(CGFloat)unit depth:(CGFloat)depth
             alpha:(CGFloat)alpha tilt:(CGFloat)tilt context:(CGContextRef)ctx {
    if (alpha < .005) return;
    CGFloat distance = MIN(1., fabs(depth));
    CGFloat height = self.flow == 1 ? MAX(.35, cos(distance*1.25)) : 1-distance*.13;
    CGPathRef glyph = (__bridge CGPathRef)_paths[d];
    CGRect box = _boundsForDigit[d];
    CGContextSaveGState(ctx);
    CGContextSetAlpha(ctx, MAX(0., MIN(1., alpha)));
    CGContextTranslateCTM(ctx,x,y);
    CGContextRotateCTM(ctx,tilt);
    CGContextScaleCTM(ctx,unit*_faceScale*(1-.035*distance),unit*_faceScale*height);
    CGContextTranslateCTM(ctx,-CGRectGetMidX(box),-CGRectGetMidY(box));
    CGContextAddPath(ctx,glyph);
    CGContextFillPath(ctx);
    CGContextRestoreGState(ctx);
}

- (void)drawRect:(NSRect)dirtyRect {
    [super drawRect:dirtyRect];
    [self prepareGlyphs];
    CGContextRef ctx = NSGraphicsContext.currentContext.CGContext;
    CGContextClearRect(ctx,dirtyRect);
    CGFloat unit = MIN(NSWidth(self.bounds)/6.35, NSHeight(self.bounds)/3.30);
    BOOL counting = self.countdown!=nil;
    if (counting) unit *= .88;
    CGFloat center = NSHeight(self.bounds)*(counting?.42:.50), x = NSMidX(self.bounds)-unit*2.76;
    NSTimeInterval absolute = NSDate.date.timeIntervalSince1970;
    time_t second = (time_t)floor(absolute);
    double fraction = absolute-floor(absolute);
    double now = NSProcessInfo.processInfo.systemUptime;
    int hour,minute,sec;
    NSInteger remaining=[self.countdown remainingAt:absolute];
    if (counting) {
        hour=(int)(remaining/3600); minute=(int)(remaining/60%60); sec=(int)(remaining%60);
        fraction=MIN(1.,MAX(0.,remaining-(self.countdown.deadline-absolute)));
    } else {
        struct tm local;
        localtime_r(&second,&local);
        hour = self.twelve ? (local.tm_hour+11)%12+1 : local.tm_hour;
        minute=local.tm_min; sec=local.tm_sec;
    }
    BOOL expired=counting && remaining==0;
    int values[] = {hour/10,hour%10,-1,minute/10,minute%10,-1,sec/10,sec%10};
    int rank = 0;
    for (int i=7;i>=0;i--) {
        if (values[i]<0) continue;
        if (!_started || _digits[i]!=values[i]) {
            _oldDigits[i] = _started ? _digits[i] : values[i];
            _oldTilt[i] = _tilt[i];
            _digits[i] = values[i];
            _tilt[i] = NewTilt();
            _start[i] = _started ? now + .045*rank : now-2;
            if (!(self.flow==0 && i==7)) rank++;
        }
    }
    _started = YES;
    BOOL dark = self.autoColor ? self.darkInk : self.theme==2;
    NSColor *ink = dark ? [NSColor colorWithSRGBRed:.05 green:.09 blue:.14 alpha:.98] :
                          [NSColor colorWithSRGBRed:.96 green:.98 blue:1 alpha:.98];
    NSColor *accent = dark ? [NSColor colorWithSRGBRed:.08 green:.18 blue:.25 alpha:.98] :
                    [NSColor colorWithSRGBRed:self.theme==3?.60:.86 green:.94 blue:1 alpha:.98];
    if (counting) {
        NSMutableParagraphStyle *style=[NSMutableParagraphStyle new];
        style.alignment=NSTextAlignmentCenter; style.lineBreakMode=NSLineBreakByTruncatingTail;
        CGFloat w=NSWidth(self.bounds), h=NSHeight(self.bounds);
        NSDictionary *attributes=@{NSFontAttributeName:[NSFont systemFontOfSize:MAX(11.,unit*.35)
            weight:NSFontWeightSemibold], NSForegroundColorAttributeName:ink, NSParagraphStyleAttributeName:style};
        [self.countdown.name drawInRect:NSMakeRect(w*.05,h*.76,w*.90,h*.22) withAttributes:attributes];
        if (expired) [@"时间到" drawInRect:NSMakeRect(w*.15,h*.035,w*.70,h*.24) withAttributes:attributes];
        CGContextSaveGState(ctx);
        CGContextClipToRect(ctx,CGRectMake(0,expired?h*.25:0,w,h*(expired?.48:.73)));
    }
    for (int i=0;i<8;i++) {
        BOOL colon = values[i]<0;
        CGFloat slot = unit*(colon?.36:.80);
        CGFloat cx = x+slot*.5;
        x += slot;
        if (colon) {
            [[accent colorWithAlphaComponent:.62+.25*cos(fraction*2*M_PI)] setFill];
            CGFloat r = unit*.045;
            for (int sign=-1;sign<=1;sign+=2)
                CGContextFillEllipseInRect(ctx,CGRectMake(cx-r,center+sign*unit*.17-r,2*r,2*r));
            continue;
        }
        [(i>=6 ? accent : ink) setFill];
        if (self.flow==0 && i==7 && !expired) {
            double p = fraction;
            for (int offset=-1;offset<=2;offset++) {
                double depth = p-offset;
                CGFloat fading = MAX(0.,1-pow(fabs(depth)/1.44,1.6));
                if (counting && (remaining-offset<0 || remaining-offset>self.countdown.duration)) continue;
                int d = (values[i]+(counting?-offset:offset)+10)%10;
                [self paintDigit:d x:cx+sin(depth*M_PI)*unit*.045 y:center+unit*depth unit:unit
                         depth:depth alpha:fading tilt:(d%2?-.037:.037) context:ctx];
            }
            continue;
        }
        double t = expired ? 1 : MIN(1.,MAX(0.,(now-_start[i])/(self.flow==3?.49:.80)));
        double p = self.flow==1 ? 1-pow(1-t,3) : .5-.5*cos(t*M_PI);
        CGFloat drift = self.flow==0 || self.flow==2 ? unit*.045*sin(p*M_PI*1.5) : 0;
        if (p<1)
            [self paintDigit:_oldDigits[i] x:cx+drift y:center+unit*p unit:unit depth:p
                       alpha:pow(1-p,.70) tilt:_oldTilt[i] context:ctx];
        CGFloat overshoot = self.flow==1 && p>.70 ? unit*.035*sin((p-.70)/.30*M_PI) : 0;
        [self paintDigit:_digits[i] x:cx-drift*(1-p) y:center-unit*(1-p)+overshoot unit:unit
                   depth:1-p alpha:pow(p,.62) tilt:_tilt[i] context:ctx];
    }
    if (counting) CGContextRestoreGState(ctx);
}
@end

@implementation ClockResizeHandle {
    NSRect _startFrame;
    NSPoint _startMouse;
}
+ (NSRect)frameByDragging:(NSSize)delta fromFrame:(NSRect)initial {
    // Project the pointer's movement onto the lower-right diagonal.
    CGFloat aspect = NSHeight(initial)/NSWidth(initial);
    CGFloat width = NSWidth(initial)+(delta.width-aspect*delta.height)/(1+aspect*aspect);
    NSSize size = [ClockView sizeForWidth:width];
    return NSMakeRect(NSMinX(initial),NSMaxY(initial)-size.height,size.width,size.height);
}
- (BOOL)mouseDownCanMoveWindow { return NO; }
- (BOOL)acceptsFirstMouse:(NSEvent *)event { return YES; }
- (void)drawRect:(NSRect)rect {
    // A faint hit surface also makes this corner reachable on a fully transparent window.
    NSRect hit = NSInsetRect(self.bounds,2,2);
    [[NSColor colorWithWhite:.5 alpha:.10] setFill];
    [[NSBezierPath bezierPathWithRoundedRect:hit xRadius:6 yRadius:6] fill];
    NSBezierPath *grip = [NSBezierPath bezierPath];
    for (NSInteger offset=0; offset<=5; offset+=5) {
        [grip moveToPoint:NSMakePoint(7+offset,7)];
        [grip lineToPoint:NSMakePoint(20,20-offset)];
    }
    grip.lineCapStyle = NSRoundLineCapStyle;
    [[NSColor colorWithWhite:0 alpha:.45] setStroke]; grip.lineWidth = 2.5; [grip stroke];
    [[NSColor colorWithWhite:1 alpha:.76] setStroke]; grip.lineWidth = 1; [grip stroke];
}
- (void)mouseDown:(NSEvent *)event {
    _startFrame = self.window.frame;
    _startMouse = [self.window convertPointToScreen:event.locationInWindow];
}
- (void)mouseDragged:(NSEvent *)event {
    NSPoint p = [self.window convertPointToScreen:event.locationInWindow];
    NSSize delta = NSMakeSize(p.x-_startMouse.x,p.y-_startMouse.y);
    [self.window setFrame:[ClockResizeHandle frameByDragging:delta fromFrame:_startFrame] display:YES];
}
- (void)mouseUp:(NSEvent *)event {
    if (self.didFinishResize && !NSEqualSizes(_startFrame.size,self.window.frame.size))
        self.didFinishResize(self.window.frame.size);
}
@end
