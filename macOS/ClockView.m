#import "ClockView.h"
#import <CoreText/CoreText.h>
#include <math.h>
#include <stdint.h>
#include <time.h>

// Control points share the built-in Windows face's original 100 x 140 design grid.
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

static const CGFloat kSlouchAngles[10] = {
    0.073, -0.087, 0.056, -0.061, 0.084, -0.073, 0.063, -0.091, 0.049, -0.070
};

static CGFloat Hash01(int n) {
    uint32_t x = (uint32_t)n;
    x = (x ^ 61u) ^ (x >> 16);
    x += x << 3;
    x ^= x >> 4;
    x *= 0x27d4eb2du;
    x ^= x >> 15;
    return (CGFloat)(x & 0xFFFFu) / 65535.0;
}

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
            CGPathAddCurveToPoint(skeleton, NULL, b.x+(c.x-a.x)/6.5, b.y+(c.y-a.y)/6.5,
                                  c.x-(d.x-b.x)/6.5, c.y-(d.y-b.y)/6.5, c.x, c.y);
        }
        start = end;
    }
    CGPathRef fill = CGPathCreateCopyByStrokingPath(skeleton, NULL, 12.2, kCGLineCapRound, kCGLineJoinRound, 2);
    CGPathRelease(skeleton);
    return fill;
}

static CGFloat ClockUnit(NSInteger scale) { return scale == 0 ? 48 : scale == 2 ? 83 : 64; }

static void FillGlyph(CGContextRef ctx, CGPathRef glyph, CGFloat alpha) {
    if (alpha <= 0.004) return;
    CGContextSaveGState(ctx);
    CGContextSetAlpha(ctx, MAX(0.0, MIN(1.0, alpha)));
    CGContextAddPath(ctx, glyph);
    CGContextFillPath(ctx);
    CGContextRestoreGState(ctx);
}

// =========================================================================
// Dedicated Scene Backgrounds (drawn inset inside the glass card when theme != 1)
// =========================================================================
static void DrawSceneBackground(CGContextRef ctx, CGRect bounds, NSInteger flow, NSInteger theme,
                                double animTime, BOOL frozen) {
    if (theme == 1) return; // PureFloating stays completely borderless & transparent
    BOOL light = (theme == 2);
    CGRect card = CGRectInset(bounds, 5.0, 5.0);
    CGFloat radius = MAX(8.0, bounds.size.width * 16.0 / (64.0 * 6.35));
    CGPathRef clip = CGPathCreateWithRoundedRect(card, radius, radius, NULL);

    CGContextSaveGState(ctx);
    CGContextAddPath(ctx, clip);
    CGContextClip(ctx);

    CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();

    if (flow == 0) {
        // LiquidStream: subtle aquatic glass depth + animated underwater caustics
        CGFloat comps[8] = {
            light ? 0.86 : 0.04, light ? 0.93 : 0.10, light ? 0.98 : 0.17, light ? 0.24 : 0.36,
            light ? 0.82 : 0.02, light ? 0.89 : 0.06, light ? 0.95 : 0.12, light ? 0.28 : 0.42
        };
        CGGradientRef grad = CGGradientCreateWithColorComponents(cs, comps, NULL, 2);
        CGContextDrawLinearGradient(ctx, grad, CGPointMake(0, CGRectGetMaxY(card)),
                                    CGPointMake(0, CGRectGetMinY(card)), 0);
        CGGradientRelease(grad);

        if (!frozen) {
            // Soft drifting caustic light ribbons
            CGFloat t = (CGFloat)animTime;
            for (int i = 0; i < 6; i++) {
                CGFloat h1 = Hash01(i * 41 + 7);
                CGFloat h2 = Hash01(i * 67 + 13);
                CGFloat cx = CGRectGetMinX(card) + card.size.width * (0.12 + 0.76 * h1)
                             + sin(t * (0.45 + 0.25 * h2) + i * 1.9) * card.size.width * 0.08;
                CGFloat cy = CGRectGetMinY(card) + card.size.height * (0.18 + 0.64 * h2)
                             + cos(t * (0.38 + 0.22 * h1) + i * 2.3) * card.size.height * 0.10;
                CGFloat rx = card.size.width * (0.14 + 0.08 * h1);
                CGFloat ry = card.size.height * (0.16 + 0.08 * h2);
                CGContextSaveGState(ctx);
                CGContextSetRGBFillColor(ctx, light ? 0.45 : 0.48, light ? 0.78 : 0.86, 1.0,
                                         light ? 0.055 : 0.075);
                CGContextFillEllipseInRect(ctx, CGRectMake(cx - rx, cy - ry, rx * 2.0, ry * 2.0));
                CGContextRestoreGState(ctx);
            }
        }
    } else if (flow == 1) {
        // EmberBurn: dark charred obsidian basalt + warm dormant hearth glow at the bottom
        CGFloat comps[12] = {
            light ? 0.88 : 0.08, light ? 0.87 : 0.07, light ? 0.86 : 0.07, light ? 0.34 : 0.68,
            light ? 0.86 : 0.11, light ? 0.83 : 0.08, light ? 0.80 : 0.07, light ? 0.38 : 0.72,
            light ? 0.92 : 0.26, light ? 0.82 : 0.10, light ? 0.76 : 0.05, light ? 0.42 : 0.76
        };
        CGFloat locs[3] = {0.0, 0.62, 1.0};
        CGGradientRef grad = CGGradientCreateWithColorComponents(cs, comps, locs, 3);
        CGContextDrawLinearGradient(ctx, grad, CGPointMake(0, CGRectGetMaxY(card)),
                                    CGPointMake(0, CGRectGetMinY(card)), 0);
        CGGradientRelease(grad);
    } else if (flow == 2) {
        // IceShards: glacial crystal glass with organic soft frost rim
        CGFloat comps[8] = {
            light ? 0.88 : 0.05, light ? 0.94 : 0.11, light ? 0.99 : 0.19, light ? 0.28 : 0.46,
            light ? 0.78 : 0.16, light ? 0.90 : 0.34, light ? 0.99 : 0.50, light ? 0.44 : 0.58
        };
        CGGradientRef grad = CGGradientCreateWithColorComponents(cs, comps, NULL, 2);
        CGPoint mid = CGPointMake(CGRectGetMidX(card), CGRectGetMidY(card));
        CGFloat maxR = hypot(card.size.width * 0.52, card.size.height * 0.52);
        CGContextDrawRadialGradient(ctx, grad, mid, maxR * 0.28, mid, maxR, kCGGradientDrawsAfterEndLocation);
        CGGradientRelease(grad);
    } else if (flow == 3) {
        // SplitFlap: brushed anodized aluminum housing + recessed bay + 4 machined corner screws
        CGFloat comps[8] = {
            light ? 0.92 : 0.17, light ? 0.93 : 0.18, light ? 0.95 : 0.21, 0.76,
            light ? 0.82 : 0.09, light ? 0.84 : 0.10, light ? 0.87 : 0.13, 0.82
        };
        CGGradientRef grad = CGGradientCreateWithColorComponents(cs, comps, NULL, 2);
        CGContextDrawLinearGradient(ctx, grad, CGPointMake(0, CGRectGetMaxY(card)),
                                    CGPointMake(0, CGRectGetMinY(card)), 0);
        CGGradientRelease(grad);

        // Recessed inner bay
        CGFloat inset = MAX(6.0, card.size.height * 0.055);
        CGRect bay = CGRectInset(card, inset, inset);
        CGPathRef bayPath = CGPathCreateWithRoundedRect(bay, MAX(4.0, radius - inset * 0.6), MAX(4.0, radius - inset * 0.6), NULL);
        CGContextSetRGBFillColor(ctx, 0, 0.01, 0.03, light ? 0.08 : 0.32);
        CGContextAddPath(ctx, bayPath);
        CGContextFillPath(ctx);
        CGContextSetRGBStrokeColor(ctx, 0, 0, 0, light ? 0.16 : 0.45);
        CGContextSetLineWidth(ctx, 1.4);
        CGContextAddPath(ctx, bayPath);
        CGContextStrokePath(ctx);
        CGPathRelease(bayPath);

        // Four machined corner screws
        CGFloat sr = MAX(2.0, MIN(3.4, inset * 0.34));
        CGFloat sx[2] = {CGRectGetMinX(card) + inset * 1.05, CGRectGetMaxX(card) - inset * 1.05};
        CGFloat sy[2] = {CGRectGetMinY(card) + inset * 1.05, CGRectGetMaxY(card) - inset * 1.05};
        for (int ix = 0; ix < 2; ix++) for (int iy = 0; iy < 2; iy++) {
            CGContextSetRGBFillColor(ctx, light ? 0.58 : 0.24, light ? 0.61 : 0.27, light ? 0.66 : 0.32, 0.82);
            CGContextFillEllipseInRect(ctx, CGRectMake(sx[ix] - sr, sy[iy] - sr, sr * 2.0, sr * 2.0));
            CGContextSetRGBStrokeColor(ctx, light ? 0.32 : 0.06, light ? 0.35 : 0.07, light ? 0.40 : 0.10, 0.8);
            CGContextSetLineWidth(ctx, 0.9);
            CGContextMoveToPoint(ctx, sx[ix] - sr * 0.65, sy[iy] - sr * 0.35);
            CGContextAddLineToPoint(ctx, sx[ix] + sr * 0.65, sy[iy] + sr * 0.35);
            CGContextStrokePath(ctx);
        }
    }

    CGColorSpaceRelease(cs);
    CGContextRestoreGState(ctx);
    CGPathRelease(clip);
}

// =========================================================================
// 1. EMBER BURN: Multi-harmonic jagged flame front, 4-stage blackbody thermal
//    glow, dynamic firelight bloom, forging reveal, and 34 rising embers & ash.
// =========================================================================
static CGFloat BurnHeight(CGFloat x, CGFloat p, int seed, CGFloat energy) {
    CGFloat wave = sin(x * 13.0 + seed * 0.7 + p * 5.2) * 0.52
                 + sin(x * 27.0 - seed * 1.1 - p * 8.4) * 0.30
                 + cos(x * 41.0 + seed * 0.3 + p * 11.0) * 0.18;
    CGFloat arch = sin((x + 0.46) / 0.92 * M_PI) * 0.032 * energy;
    return -0.58 + 1.18 * p + arch + 0.052 * energy * wave;
}

static void StrokeBurnLine(CGContextRef ctx, CGFloat p, int seed, CGFloat energy, CGFloat yOffset,
                           CGFloat r, CGFloat g, CGFloat b, CGFloat a, CGFloat width) {
    CGContextBeginPath(ctx);
    for (int j = 0; j <= 28; j++) {
        CGFloat x = -0.46 + 0.92 * j / 28.0;
        CGFloat y = BurnHeight(x, p, seed, energy) + yOffset;
        if (j == 0) CGContextMoveToPoint(ctx, x, y);
        else CGContextAddLineToPoint(ctx, x, y);
    }
    CGContextSetRGBStrokeColor(ctx, r, g, b, a);
    CGContextSetLineWidth(ctx, width);
    CGContextSetLineJoin(ctx, kCGLineJoinRound);
    CGContextSetLineCap(ctx, kCGLineCapRound);
    CGContextStrokePath(ctx);
}

static void PaintEmbers(CGContextRef ctx, CGPathRef old, CGPathRef next, CGFloat t, int seed, BOOL hasBg, BOOL darkInk) {
    CGFloat p = t * t * (3.0 - 2.0 * t);
    CGFloat energy = pow(sin(t * M_PI), 0.70);

    // Dynamic ambient firelight on obsidian background
    if (hasBg && !darkInk && energy > 0.04) {
        CGContextSaveGState(ctx);
        CGFloat fy = -0.56 + 1.12 * p;
        CGColorSpaceRef cs = CGColorSpaceCreateDeviceRGB();
        CGFloat comps[8] = {1.0, 0.36, 0.07, 0.36 * energy, 1.0, 0.24, 0.04, 0.0};
        CGGradientRef grad = CGGradientCreateWithColorComponents(cs, comps, NULL, 2);
        CGContextDrawRadialGradient(ctx, grad, CGPointMake(0, fy), 0.02, CGPointMake(0, fy), 0.52, 0);
        CGGradientRelease(grad);
        CGColorSpaceRelease(cs);
        CGContextRestoreGState(ctx);
    }

    // Incoming glyph emerging below the burn front (cooling from warm ember light)
    CGContextSaveGState(ctx);
    CGContextBeginPath(ctx);
    CGContextMoveToPoint(ctx, -0.48, -0.85);
    for (int j = 0; j <= 28; j++) {
        CGFloat x = -0.48 + 0.96 * j / 28.0;
        CGContextAddLineToPoint(ctx, x, BurnHeight(x, p, seed, energy));
    }
    CGContextAddLineToPoint(ctx, 0.48, -0.85);
    CGContextClosePath(ctx);
    CGContextClip(ctx);
    FillGlyph(ctx, next, pow(t, 0.65));
    CGContextAddPath(ctx, next);
    CGContextClip(ctx);
    StrokeBurnLine(ctx, p, seed, energy, -0.01, 1.0, 0.50, 0.14, 0.78 * energy, 0.095);
    StrokeBurnLine(ctx, p, seed, energy, -0.005, 1.0, 0.88, 0.56, 0.90 * energy, 0.032);
    CGContextRestoreGState(ctx);

    // Departing glyph above the burn front with 4-stage blackbody thermal gradient
    CGContextSaveGState(ctx);
    CGContextBeginPath(ctx);
    CGContextMoveToPoint(ctx, -0.48, 0.85);
    for (int j = 0; j <= 28; j++) {
        CGFloat x = -0.48 + 0.96 * j / 28.0;
        CGContextAddLineToPoint(ctx, x, BurnHeight(x, p, seed, energy));
    }
    CGContextAddLineToPoint(ctx, 0.48, 0.85);
    CGContextClosePath(ctx);
    CGContextClip(ctx);
    FillGlyph(ctx, old, 1.0);
    CGContextAddPath(ctx, old);
    CGContextClip(ctx);
    // Stage 1: charred soot -> Stage 2: deep crimson -> Stage 3: blazing orange -> Stage 4: white-gold core
    StrokeBurnLine(ctx, p, seed, energy, 0.030, 0.11, 0.05, 0.03, 0.84 * energy, 0.16);
    StrokeBurnLine(ctx, p, seed, energy, 0.015, 0.84, 0.16, 0.04, 0.92 * energy, 0.105);
    StrokeBurnLine(ctx, p, seed, energy, 0.005, 1.00, 0.46, 0.08, 0.96 * energy, 0.062);
    StrokeBurnLine(ctx, p, seed, energy, 0.000, 1.00, 0.95, 0.72, 1.00 * energy, 0.026);
    CGContextRestoreGState(ctx);

    // 34 turbulent rising embers with glow halos + dark ash flakes
    CGContextSaveGState(ctx);
    for (int j = 0; j < 34; j++) {
        CGFloat h1 = Hash01(seed * 17 + j * 43);
        CGFloat h2 = Hash01(seed * 31 + j * 61);
        CGFloat h3 = Hash01(seed * 13 + j * 89);
        CGFloat phase = fmod(t * (1.25 + h2 * 1.10) + h1 * 0.90, 1.0);
        CGFloat life = energy * sin(phase * M_PI);
        if (life <= 0.04) continue;

        CGFloat sx = (h1 - 0.5) * 0.74;
        CGFloat sy = BurnHeight(sx, p, seed, energy);
        CGFloat drift = sin(phase * 7.2 + j * 1.9) * (0.04 + 0.05 * h2);
        CGFloat px = sx + drift;
        CGFloat py = sy + (0.04 + (0.38 + 0.34 * h3) * phase * (0.75 + 0.45 * phase));

        if (j % 6 == 0) {
            CGFloat ar = 0.011 + 0.009 * h2;
            CGContextSetRGBFillColor(ctx, 0.14, 0.10, 0.09, 0.62 * life);
            CGContextFillEllipseInRect(ctx, CGRectMake(px - ar, py - ar * 0.6, ar * 2.0, ar * 1.2));
        } else {
            CGFloat r = (0.009 + 0.009 * h2) * (1.0 - 0.52 * phase);
            CGContextSetRGBFillColor(ctx, 1.0, 0.42, 0.09, 0.28 * life);
            CGContextFillEllipseInRect(ctx, CGRectMake(px - r * 2.4, py - r * 2.4, r * 4.8, r * 4.8));

            CGFloat cg = phase < 0.32 ? (0.96 - 0.34 * (phase / 0.32)) : (0.62 - 0.46 * ((phase - 0.32) / 0.68));
            CGFloat cb = phase < 0.32 ? (0.78 - 0.62 * (phase / 0.32)) : 0.06;
            CGContextSetRGBStrokeColor(ctx, 1.0, cg, cb, life);
            CGContextSetLineWidth(ctx, r * 1.45);
            CGContextSetLineCap(ctx, kCGLineCapRound);
            CGContextMoveToPoint(ctx, px, py);
            CGContextAddLineToPoint(ctx, px - drift * 0.12, py - r * (2.4 + 2.2 * h3));
            CGContextStrokePath(ctx);
        }
    }
    CGContextRestoreGState(ctx);
}

// =========================================================================
// 2. ICE SHARDS: Branching frost cracks -> 16 irregular angular crystal shards
//    tumbling in 3D with Fresnel specular glint + diamond ice dust -> re-assembly.
// =========================================================================
static void PaintIce(CGContextRef ctx, CGPathRef old, CGPathRef next, CGFloat t, int seed, BOOL darkInk) {
    CGFloat crackPhase = MIN(1.0, t / 0.25);
    CGFloat shatter = MAX(0.0, MIN(1.0, (t - 0.19) / 0.59));
    CGFloat assemble = MAX(0.0, MIN(1.0, (t - 0.35) / 0.65));

    CGFloat iceR = darkInk ? 0.10 : 0.48, iceG = darkInk ? 0.46 : 0.85, iceB = darkInk ? 0.68 : 1.0;
    CGFloat glR = darkInk ? 0.72 : 0.94, glG = darkInk ? 0.92 : 0.99, glB = 1.0;

    // 16 angular crystal sectors around off-center impact point (2 concentric rings = rich Voronoi look)
    CGPoint epi = CGPointMake(-0.06, 0.03);
    const int sectors = 16;

    // Departing digit: freeze, fracture, shatter in 3D
    if (shatter < 1.0) {
        CGFloat burst = shatter * shatter * (1.75 - 0.75 * shatter);
        CGFloat opacity = shatter <= 0.0 ? 1.0 : pow(1.0 - shatter, 0.80);

        for (int i = 0; i < sectors; i++) {
            CGFloat a0 = (i * 2.0 * M_PI / sectors) + (Hash01(i * 19 + 3) - 0.5) * 0.16;
            CGFloat a1 = ((i + 1) * 2.0 * M_PI / sectors) + (Hash01((i + 1) % sectors * 19 + 3) - 0.5) * 0.16;
            CGFloat midA = (a0 + a1) * 0.5;
            CGFloat rOuter = 0.72;
            CGPoint pA = CGPointMake(epi.x + cos(a0) * rOuter, epi.y + sin(a0) * rOuter);
            CGPoint pB = CGPointMake(epi.x + cos(a1) * rOuter, epi.y + sin(a1) * rOuter);
            CGPoint centroid = CGPointMake(epi.x + cos(midA) * 0.28, epi.y + sin(midA) * 0.28);

            CGFloat h1 = Hash01(seed + i * 29);
            CGFloat h2 = Hash01(seed + i * 53 + 11);
            CGFloat h3 = Hash01(seed + i * 71 + 3);

            CGFloat tx = cos(midA) * (0.28 + 0.24 * h1) * burst;
            CGFloat ty = sin(midA) * (0.28 + 0.22 * h2) * burst - (0.26 + 0.18 * h3) * shatter * shatter;

            // 3D tumbling (roll + pitch + yaw) & Fresnel specular facet flash
            CGFloat roll = ((i % 2 == 0) ? 1.0 : -1.0) * (0.36 + 0.65 * h1) * burst;
            CGFloat pitch = ((i % 3 == 0) ? 1.0 : -1.0) * (0.75 + 1.10 * h2) * shatter;
            CGFloat yaw = ((i % 2 == 0) ? -1.0 : 1.0) * (0.65 + 1.00 * h3) * shatter;
            CGFloat sx = MAX(0.24, fabs(cos(yaw))) * (1.0 - 0.14 * shatter);
            CGFloat sy = MAX(0.24, fabs(cos(pitch))) * (1.0 - 0.14 * shatter);
            CGFloat glint = pow(fabs(sin(pitch * 1.4 + yaw * 1.1 + i * 0.7)), 5.0) * shatter;

            CGContextSaveGState(ctx);
            CGContextTranslateCTM(ctx, centroid.x + tx, centroid.y + ty);
            CGContextRotateCTM(ctx, roll);
            CGContextScaleCTM(ctx, sx, sy);
            CGContextTranslateCTM(ctx, -centroid.x, -centroid.y);

            CGContextBeginPath(ctx);
            CGContextMoveToPoint(ctx, epi.x, epi.y);
            CGContextAddLineToPoint(ctx, pA.x, pA.y);
            CGContextAddLineToPoint(ctx, pB.x, pB.y);
            CGContextClosePath(ctx);
            CGContextClip(ctx);

            // Base frosted shard + Fresnel specular flash
            CGFloat frostMix = MIN(0.90, crackPhase * 0.45 + shatter * 0.55);
            FillGlyph(ctx, old, opacity * (1.0 - frostMix * 0.55));
            CGContextSaveGState(ctx);
            CGContextSetRGBFillColor(ctx,
                iceR + (glR - iceR) * glint * 0.85,
                iceG + (glG - iceG) * glint * 0.85,
                iceB + (glB - iceB) * glint * 0.85,
                opacity * (frostMix * 0.82 + glint * 0.45));
            CGContextAddPath(ctx, old);
            CGContextFillPath(ctx);
            CGContextRestoreGState(ctx);

            // Crisp crystalline fracture edge highlight
            CGContextAddPath(ctx, old);
            CGContextClip(ctx);
            CGContextSetRGBStrokeColor(ctx, glR, glG, glB, MIN(1.0, crackPhase * 0.85 + glint * 0.7) * opacity);
            CGContextSetLineWidth(ctx, 0.018);
            CGContextMoveToPoint(ctx, epi.x, epi.y);
            CGContextAddLineToPoint(ctx, pA.x, pA.y);
            CGContextStrokePath(ctx);

            CGContextRestoreGState(ctx);
        }

        // Branching lightning frost crack flash before shatter
        CGFloat crackFlash = sin(MAX(0.0, MIN(1.0, (t - 0.03) / 0.27)) * M_PI);
        if (crackFlash > 0.05) {
            CGContextSaveGState(ctx);
            CGContextAddPath(ctx, old);
            CGContextClip(ctx);
            for (int r = 0; r < 7; r++) {
                CGFloat ang = r * (2.0 * M_PI / 7.0) + (seed % 7) * 0.22;
                CGFloat reach = 0.34 + 0.34 * crackPhase;
                CGPoint p1 = CGPointMake(epi.x + cos(ang + 0.16) * reach * 0.44, epi.y + sin(ang + 0.16) * reach * 0.44);
                CGPoint p2 = CGPointMake(epi.x + cos(ang - 0.08) * reach * 0.82, epi.y + sin(ang - 0.08) * reach * 0.82);
                CGPoint fork = CGPointMake(p1.x + cos(ang + 0.62) * reach * 0.36, p1.y + sin(ang + 0.62) * reach * 0.36);

                CGContextSetRGBStrokeColor(ctx, iceR, iceG, iceB, 0.70 * crackFlash);
                CGContextSetLineWidth(ctx, 0.042);
                CGContextMoveToPoint(ctx, epi.x, epi.y);
                CGContextAddLineToPoint(ctx, p1.x, p1.y);
                CGContextAddLineToPoint(ctx, p2.x, p2.y);
                CGContextMoveToPoint(ctx, p1.x, p1.y);
                CGContextAddLineToPoint(ctx, fork.x, fork.y);
                CGContextStrokePath(ctx);

                CGContextSetRGBStrokeColor(ctx, glR, glG, glB, 0.96 * crackFlash);
                CGContextSetLineWidth(ctx, 0.015);
                CGContextMoveToPoint(ctx, epi.x, epi.y);
                CGContextAddLineToPoint(ctx, p1.x, p1.y);
                CGContextAddLineToPoint(ctx, p2.x, p2.y);
                CGContextMoveToPoint(ctx, p1.x, p1.y);
                CGContextAddLineToPoint(ctx, fork.x, fork.y);
                CGContextStrokePath(ctx);
            }
            CGContextRestoreGState(ctx);
        }
    }

    // 24 sparkling diamond ice dust crystals
    CGFloat dustEnv = sin(MAX(0.0, MIN(1.0, (t - 0.15) / 0.74)) * M_PI);
    if (dustEnv > 0.05) {
        CGContextSaveGState(ctx);
        for (int k = 0; k < 24; k++) {
            CGFloat h1 = Hash01(seed * 19 + k * 37);
            CGFloat h2 = Hash01(seed * 11 + k * 53);
            CGFloat h3 = Hash01(seed * 23 + k * 71);
            CGFloat ang = h1 * 2.0 * M_PI;
            CGFloat dist = (0.10 + 0.42 * shatter) * (0.55 + 0.72 * h2);
            CGFloat px = cos(ang) * dist;
            CGFloat py = sin(ang) * dist - 0.18 * shatter * shatter;
            CGFloat twinkle = 0.55 + 0.45 * sin(t * 24.0 + k * 1.7);
            CGFloat sr = (0.008 + 0.011 * h3) * dustEnv * twinkle;
            CGContextSetRGBFillColor(ctx, k % 2 == 0 ? glR : iceR, k % 2 == 0 ? glG : iceG, k % 2 == 0 ? glB : iceB, 0.92 * dustEnv * twinkle);
            CGContextBeginPath(ctx);
            CGContextMoveToPoint(ctx, px, py + sr * 1.5);
            CGContextAddLineToPoint(ctx, px + sr, py);
            CGContextAddLineToPoint(ctx, px, py - sr * 1.5);
            CGContextAddLineToPoint(ctx, px - sr, py);
            CGContextClosePath(ctx);
            CGContextFillPath(ctx);
        }
        CGContextRestoreGState(ctx);
    }

    // Incoming digit: shards converge from frost mist and thaw into crisp ink
    if (assemble > 0.0) {
        CGFloat inv = 1.0 - assemble;
        CGFloat spread = inv * inv * inv;
        CGFloat arriveAlpha = pow(assemble, 0.52);

        for (int i = 0; i < sectors; i++) {
            CGFloat a0 = (i * 2.0 * M_PI / sectors) + (Hash01(i * 19 + 3) - 0.5) * 0.16;
            CGFloat a1 = ((i + 1) * 2.0 * M_PI / sectors) + (Hash01((i + 1) % sectors * 19 + 3) - 0.5) * 0.16;
            CGFloat midA = (a0 + a1) * 0.5;
            CGPoint pA = CGPointMake(epi.x + cos(a0) * 0.72, epi.y + sin(a0) * 0.72);
            CGPoint pB = CGPointMake(epi.x + cos(a1) * 0.72, epi.y + sin(a1) * 0.72);
            CGFloat h1 = Hash01(seed + i * 31 + 5);

            CGContextSaveGState(ctx);
            CGContextTranslateCTM(ctx, cos(midA) * (0.24 + 0.16 * h1) * spread,
                                  sin(midA) * (0.22 + 0.14 * h1) * spread + 0.08 * spread);
            CGContextRotateCTM(ctx, ((i % 2 == 0) ? -1.0 : 1.0) * (0.28 + 0.25 * h1) * spread);
            CGContextBeginPath(ctx);
            CGContextMoveToPoint(ctx, epi.x, epi.y);
            CGContextAddLineToPoint(ctx, pA.x, pA.y);
            CGContextAddLineToPoint(ctx, pB.x, pB.y);
            CGContextClosePath(ctx);
            CGContextClip(ctx);
            FillGlyph(ctx, next, arriveAlpha);
            if (inv > 0.05) {
                CGContextSetRGBFillColor(ctx, iceR, iceG, iceB, inv * 0.62 * arriveAlpha);
                CGContextAddPath(ctx, next);
                CGContextFillPath(ctx);
            }
            CGContextRestoreGState(ctx);
        }
    }
}

// =========================================================================
// 3. SPLIT FLAP: 3D perspective trapezoidal flap, visible card edge thickness,
//    gravity drop + two-stage damped bounce, cast shadows, and hinge barrels.
// =========================================================================
static void PaintFlapHalf3D(CGContextRef ctx, CGPathRef glyph, BOOL upper, CGFloat scaleY, CGFloat flare,
                            BOOL hasCard, BOOL lightCard, CGFloat shade, CGFloat glint) {
    if (scaleY < 0.004) return;
    CGFloat halfH = 0.52;
    CGFloat cardW = 0.37;
    CGFloat yEdge = upper ? (halfH * scaleY) : (-halfH * scaleY);

    if (hasCard) {
        CGContextSaveGState(ctx);
        CGFloat baseG = (lightCard ? 0.90 : 0.13) + (upper ? 0.04 : -0.02) - shade + glint;
        baseG = MAX(0.03, MIN(0.98, baseG));

        // 3D perspective trapezoid card body
        CGContextBeginPath(ctx);
        if (upper) {
            CGContextMoveToPoint(ctx, -cardW * flare, yEdge);
            CGContextAddLineToPoint(ctx, cardW * flare, yEdge);
            CGContextAddLineToPoint(ctx, cardW, 0);
            CGContextAddLineToPoint(ctx, -cardW, 0);
        } else {
            CGContextMoveToPoint(ctx, -cardW, 0);
            CGContextAddLineToPoint(ctx, cardW, 0);
            CGContextAddLineToPoint(ctx, cardW * flare, yEdge);
            CGContextAddLineToPoint(ctx, -cardW * flare, yEdge);
        }
        CGContextClosePath(ctx);
        CGContextSetRGBFillColor(ctx, baseG, baseG + 0.01, baseG + 0.03, 0.97);
        CGContextFillPath(ctx);

        // Visible 3D card thickness lip on the swinging outer edge
        if (flare > 1.005) {
            CGFloat lip = 0.028 * ((flare - 1.0) / 0.13);
            CGFloat lipY = upper ? yEdge : (yEdge - lip);
            CGContextSetRGBFillColor(ctx, lightCard ? 0.70 : 0.32, lightCard ? 0.73 : 0.36, lightCard ? 0.78 : 0.43, 0.92);
            CGContextFillRect(ctx, CGRectMake(-cardW * flare, lipY, cardW * 2.0 * flare, lip));
        }
        CGContextRestoreGState(ctx);
    }

    // Glyph clipped & scaled to match the 3D flap
    CGContextSaveGState(ctx);
    CGFloat fit = hasCard ? 0.88 : 1.0;
    CGFloat avgX = fit * (1.0 + (flare - 1.0) * 0.52);
    CGContextClipToRect(ctx, CGRectMake(-0.42 * flare, upper ? 0 : yEdge, 0.84 * flare, fabs(yEdge)));
    CGContextScaleCTM(ctx, avgX, scaleY * fit);
    FillGlyph(ctx, glyph, MAX(0.25, 1.0 - shade * 1.8));
    CGContextRestoreGState(ctx);
}

static void PaintFlap(CGContextRef ctx, CGPathRef old, CGPathRef next, CGFloat t, BOOL hasCard, BOOL lightCard) {
    // Gravity-accelerated mechanical fold + two-stage damped bounce
    CGFloat flipProgress;
    if (t <= 0.0) flipProgress = 0.0;
    else if (t >= 1.0) flipProgress = 1.0;
    else if (t < 0.78) {
        CGFloat u = t / 0.78;
        flipProgress = u < 0.45
            ? (u / 0.45) * (u / 0.45) * 0.45
            : 0.45 + (1.0 - pow(1.0 - (u - 0.45) / 0.55, 2.6)) * 0.55;
    } else {
        CGFloat b = (t - 0.78) / 0.22;
        flipProgress = 1.0 - fabs(sin(b * M_PI * 1.5)) * 0.045 * pow(1.0 - b, 1.35);
    }

    CGFloat angle = flipProgress * M_PI;
    CGFloat cosA = cos(angle);
    CGFloat sinA = sin(angle);
    CGFloat flare = 1.0 + 0.13 * sinA;

    // Static top (next) and bottom (old while flipping, next when settled)
    CGContextSaveGState(ctx);
    if (!hasCard && t>0 && t<1 && cosA>0)
        CGContextClipToRect(ctx,CGRectMake(-.5,.52*cosA,1,.53-.52*cosA));
    PaintFlapHalf3D(ctx, t<=0 ? old : next, YES, 1.0, 1.0, hasCard, lightCard,
                    t>0 && cosA>0 ? 0.07*cosA : 0.0, 0.0);
    CGContextRestoreGState(ctx);
    CGContextSaveGState(ctx);
    if (!hasCard && t>0 && t<1 && cosA<0)
        CGContextClipToRect(ctx,CGRectMake(-.5,-.53,1,.53+.52*cosA));
    PaintFlapHalf3D(ctx, t < 1.0 ? old : next, NO, 1.0, 1.0, hasCard, lightCard, cosA < 0 ? 0.08 * (1.0 + cosA) : 0.0, 0.0);
    CGContextRestoreGState(ctx);

    if (t > 0.0 && t < 1.0) {
        if (cosA >= 0.0) {
            PaintFlapHalf3D(ctx, old, YES, cosA, flare, hasCard, lightCard, 0.11 * (1.0 - cosA), 0.0);
        } else {
            CGFloat k = -cosA;
            CGFloat glint = pow(sin(k * M_PI), 1.6) * 0.14;
            PaintFlapHalf3D(ctx, next, NO, k, flare, hasCard, lightCard, 0.05 * (1.0 - k), glint);
        }
    }

    // Center mechanical hinge seam + side barrels
    CGContextSaveGState(ctx);
    if (!hasCard) {
        CGContextClipToRect(ctx, CGRectMake(-0.42, -0.011, 0.84, 0.022));
        CGContextSetRGBFillColor(ctx, lightCard ? 0.25 : 0.03, lightCard ? 0.28 : 0.04, lightCard ? 0.34 : 0.07, 0.75);
        CGContextAddPath(ctx, t < 0.5 ? old : next);
        CGContextFillPath(ctx);
    } else {
        CGContextSetRGBFillColor(ctx, 0, 0, 0, 0.78);
        CGContextFillRect(ctx, CGRectMake(-0.37, -0.011, 0.74, 0.022));
        CGContextSetRGBFillColor(ctx, 1, 1, 1, lightCard ? 0.42 : 0.14);
        CGContextFillRect(ctx, CGRectMake(-0.35, -0.016, 0.70, 0.006));
        CGContextSetRGBFillColor(ctx, lightCard ? 0.52 : 0.28, lightCard ? 0.56 : 0.33, lightCard ? 0.62 : 0.40, 0.92);
        for (int side = -1; side <= 1; side += 2)
            CGContextFillRect(ctx, CGRectMake(side * 0.355 - 0.016, -0.034, 0.032, 0.068));
    }
    CGContextRestoreGState(ctx);
}

@implementation ClockView {
    NSArray *_paths;
    NSArray *_unitPaths;
    NSString *_cachedName;
    CGFloat _faceScale;
    CGRect _boundsForDigit[10];
    int _digits[8], _oldDigits[8];
    double _start[8];
    BOOL _started;
    BOOL _isAnimating;
}

- (BOOL)isAnimating { return _isAnimating; }

+ (NSSize)sizeForScale:(NSInteger)scale {
    return [self sizeForWidth:ClockUnit(scale)*6.35];
}
+ (NSSize)sizeForWidth:(CGFloat)width {
    CGFloat w = isfinite(width) ? MAX(160,MIN(2400,width)) : ClockUnit(1)*6.35;
    return NSMakeSize(w,w*3.30/6.35);
}
- (BOOL)isOpaque { return NO; }
- (BOOL)mouseDownCanMoveWindow { return YES; }
- (NSTimeInterval)wallTime { return NSDate.date.timeIntervalSince1970; }
- (NSTimeInterval)animationTime { return NSProcessInfo.processInfo.systemUptime; }
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
    NSMutableArray *units=[NSMutableArray arrayWithCapacity:10];
    for (int d=0;d<10;d++) {
        CGRect box=_boundsForDigit[d];
        CGAffineTransform matrix=CGAffineTransformMakeRotation(hand ? kSlouchAngles[d] : 0);
        matrix=CGAffineTransformScale(matrix,_faceScale,_faceScale);
        matrix=CGAffineTransformTranslate(matrix,-CGRectGetMidX(box),-CGRectGetMidY(box));
        CGPathRef path=CGPathCreateCopyByTransformingPath((__bridge CGPathRef)paths[d],&matrix);
        [units addObject:CFBridgingRelease(path)];
    }
    _unitPaths=units;
}

- (CGFloat)slouchForDigit:(int)d {
    BOOL hand = [_cachedName isEqualToString:@"随性手写（内置）"];
    return hand ? kSlouchAngles[d % 10] : 0.0;
}

- (void)paintDigit:(int)d x:(CGFloat)x y:(CGFloat)y unit:(CGFloat)unit depth:(CGFloat)depth
             alpha:(CGFloat)alpha context:(CGContextRef)ctx {
    if (alpha < .005) return;
    CGFloat distance = MIN(1., fabs(depth));
    CGFloat cosT = cos(distance * 1.15);
    CGFloat height = 1.0 - 0.28 * (1.0 - cosT);
    CGPathRef glyph = (__bridge CGPathRef)_paths[d];
    CGRect box = _boundsForDigit[d];
    CGContextSaveGState(ctx);
    CGContextSetAlpha(ctx, MAX(0., MIN(1., alpha)));
    CGContextTranslateCTM(ctx, x, y);
    CGContextRotateCTM(ctx, [self slouchForDigit:d]);
    CGContextScaleCTM(ctx, unit * _faceScale * (1.0 - 0.04 * (1.0 - height)), unit * _faceScale * height);
    CGContextTranslateCTM(ctx, -CGRectGetMidX(box), -CGRectGetMidY(box));
    CGContextAddPath(ctx, glyph);
    CGContextFillPath(ctx);
    CGContextRestoreGState(ctx);
}

- (void)paintEffectAtSlot:(int)slot center:(CGPoint)center unit:(CGFloat)unit progress:(CGFloat)p
               darkInk:(BOOL)dark context:(CGContextRef)ctx {
    if (self.flow != 3 && (p >= 1.0 || p <= 0.0)) {
        [self paintDigit:p >= 1.0 ? _digits[slot] : _oldDigits[slot] x:center.x y:center.y unit:unit depth:0 alpha:1.0
                 context:ctx];
        return;
    }
    CGPathRef next = (__bridge CGPathRef)_unitPaths[_digits[slot]];
    CGPathRef old = (__bridge CGPathRef)_unitPaths[_oldDigits[slot]];
    CGContextSaveGState(ctx);
    CGContextTranslateCTM(ctx, center.x, center.y);
    CGContextScaleCTM(ctx, unit, unit);
    // Strict horizontal column clip prevents debris from touching adjacent fixed digits.
    CGContextClipToRect(ctx, CGRectMake(-0.44, -1.25, 0.88, 2.50));
    int seed = slot * 13 + _oldDigits[slot] * 7 + _digits[slot] * 19;
    BOOL hasCard = (self.theme != 1);
    switch (self.flow) {
        case 1: PaintEmbers(ctx, old, next, p, seed, hasCard, dark); break;
        case 2: PaintIce(ctx, old, next, p, seed, dark); break;
        case 3: PaintFlap(ctx, old, next, p, hasCard, dark); break;
    }
    CGContextRestoreGState(ctx);
}

- (void)drawRect:(NSRect)dirtyRect {
    [super drawRect:dirtyRect];
    [self prepareGlyphs];
    CGContextRef ctx = NSGraphicsContext.currentContext.CGContext;
    CGContextClearRect(ctx, dirtyRect);
    CGFloat unit = MIN(NSWidth(self.bounds)/6.35, NSHeight(self.bounds)/3.30);
    BOOL counting = self.countdown != nil;
    if (counting) unit *= .88;
    CGFloat center = NSHeight(self.bounds) * (counting ? .42 : .50);
    CGFloat x = NSMidX(self.bounds) - unit * 2.76;
    NSTimeInterval absolute = self.wallTime;
    time_t second = (time_t)floor(absolute);
    double fraction = absolute - floor(absolute);
    double now = self.animationTime;
    int hour, minute, sec;
    NSInteger remaining = [self.countdown remainingAt:absolute];
    if (counting) {
        hour = (int)[ClockCountdown displayHoursForRemaining:remaining];
        minute = (int)(remaining / 60 % 60);
        sec = (int)(remaining % 60);
        fraction = MIN(1., MAX(0., remaining - (self.countdown.deadline - absolute)));
    } else {
        struct tm local;
        localtime_r(&second, &local);
        hour = self.twelve ? (local.tm_hour + 11) % 12 + 1 : local.tm_hour;
        minute = local.tm_min;
        sec = local.tm_sec;
    }
    BOOL expired = counting && remaining == 0;
    NSInteger flow = self.flow;
    if (flow < 0 || flow > 3) flow = 0;

    // Dedicated scene background (skipped in PureFloating theme == 1)
    DrawSceneBackground(ctx, self.bounds, flow, self.theme, now, expired || counting);

    int values[] = {hour/10, hour%10, -1, minute/10, minute%10, -1, sec/10, sec%10};
    int rank = 0;
    for (int i = 7; i >= 0; i--) {
        if (values[i] < 0) continue;
        if (!_started || _digits[i] != values[i]) {
            _oldDigits[i] = _started ? _digits[i] : values[i];
            _digits[i] = values[i];
            _start[i] = _started ? now + 0.052 * rank : now - 2.0;
            if (!(flow == 0 && i == 7)) rank++;
        }
    }
    _started = YES;

    BOOL dark = self.autoColor ? self.darkInk : self.theme == 2;
    NSColor *ink = dark ? [NSColor colorWithSRGBRed:.05 green:.09 blue:.14 alpha:.98] :
                          [NSColor colorWithSRGBRed:.96 green:.98 blue:1 alpha:.98];
    NSColor *accent = dark ? [NSColor colorWithSRGBRed:.08 green:.18 blue:.25 alpha:.98] :
                    [NSColor colorWithSRGBRed:self.theme==3?.60:.86 green:.94 blue:1 alpha:.98];

    if (counting) {
        NSMutableParagraphStyle *style = [NSMutableParagraphStyle new];
        style.alignment = NSTextAlignmentCenter;
        style.lineBreakMode = NSLineBreakByTruncatingTail;
        CGFloat w = NSWidth(self.bounds), h = NSHeight(self.bounds);
        NSDictionary *attributes = @{
            NSFontAttributeName: [NSFont systemFontOfSize:MAX(11., unit * .35) weight:NSFontWeightSemibold],
            NSForegroundColorAttributeName: ink,
            NSParagraphStyleAttributeName: style
        };
        [[self.countdown captionForRemaining:remaining] drawInRect:NSMakeRect(w*.05, h*.76, w*.90, h*.22) withAttributes:attributes];
        if (expired) [@"时间到" drawInRect:NSMakeRect(w*.15, h*.035, w*.70, h*.24) withAttributes:attributes];
        CGContextSaveGState(ctx);
        CGContextClipToRect(ctx, CGRectMake(0, expired ? h * .25 : 0, w, h * (expired ? .48 : .73)));
    }

    BOOL anyAnimating = (!expired && flow == 0 && !counting);
    double effectDuration = (flow == 3) ? 0.64 : (flow == 2 ? 0.88 : 0.86);

    for (int i = 0; i < 8; i++) {
        BOOL colon = values[i] < 0;
        CGFloat slot = unit * (colon ? .36 : .80);
        CGFloat cx = x + slot * .5;
        x += slot;
        if (colon) {
            CGFloat colonAlpha = (flow >= 1) ? 0.85 : (0.58 + 0.34 * cos(fraction * 2.0 * M_PI));
            [[accent colorWithAlphaComponent:colonAlpha] setFill];
            CGFloat r = unit * .045;
            for (int sign = -1; sign <= 1; sign += 2)
                CGContextFillEllipseInRect(ctx, CGRectMake(cx - r, center + sign * unit * .17 - r, 2 * r, 2 * r));
            continue;
        }
        [(i >= 6 ? accent : ink) setFill];

        if (flow == 0 && counting) {
            [self paintDigit:_digits[i] x:cx y:center unit:unit depth:0 alpha:1 context:ctx];
            continue;
        }
        if (flow == 0 && i == 7) {
            // C1-continuous liquid stream on seconds ones-digit + rising micro-bubbles
            double p = fraction - 0.095 * sin(2.0 * M_PI * fraction);
            for (int offset = -1; offset <= 2; offset++) {
                double depth = p - offset;
                CGFloat fading = MAX(0., 1.0 - pow(fabs(depth) / 1.44, 1.55));
                int d = (values[i] + offset + 10) % 10;
                CGFloat wave = sin(depth * M_PI * 1.4 - now * 3.8) * unit * 0.042 * fabs(depth);
                [self paintDigit:d x:cx + wave y:center + unit * 1.05 * depth unit:unit
                           depth:depth alpha:fading context:ctx];
            }
            if (!counting) {
                CGContextSaveGState(ctx);
                for (int b = 0; b < 6; b++) {
                    CGFloat h1 = Hash01(b * 43 + 17);
                    CGFloat h2 = Hash01(b * 79 + 31);
                    CGFloat cyc = fmod(now * (0.28 + 0.24 * h1) + h2, 1.0);
                    CGFloat env = sin(cyc * M_PI);
                    if (env < 0.05) continue;
                    CGFloat bx = cx + (h1 - 0.5) * unit * 0.72 + sin(cyc * 7.5 + b * 2.1) * 2.5;
                    CGFloat by = center - unit * 1.2 + cyc * unit * 2.4;
                    CGFloat br = MAX(1.0, unit * (0.018 + 0.015 * h2));
                    [[accent colorWithAlphaComponent:0.35 * env] setStroke];
                    CGContextSetLineWidth(ctx, 0.9);
                    CGContextStrokeEllipseInRect(ctx, CGRectMake(bx - br, by - br, br * 2.0, br * 2.0));
                }
                CGContextRestoreGState(ctx);
            }
            continue;
        }

        double elapsed = now - _start[i];
        double t = expired ? 1.0 : MIN(1.0, MAX(0.0, elapsed / effectDuration));
        if (!expired && elapsed < effectDuration + 0.04) anyAnimating = YES;

        if (flow >= 1 && flow <= 3) {
            [self paintEffectAtSlot:i center:CGPointMake(cx, center) unit:unit progress:t darkInk:dark context:ctx];
            continue;
        }

        // LiquidStream stepped digits (hours, minutes, tens-of-seconds)
        double sineEase = 0.5 - 0.5 * cos(t * M_PI);
        double decelEase = 1.0 - pow(1.0 - t, 2.2);
        double p = sineEase * 0.68 + decelEase * 0.32;
        CGFloat drift = unit * 0.038 * sin(p * M_PI * 1.4);
        if (p < 1.0)
            [self paintDigit:_oldDigits[i] x:cx + drift y:center + unit * 1.05 * p unit:unit depth:p
                       alpha:pow(1.0 - p, 0.62) context:ctx];
        [self paintDigit:_digits[i] x:cx - drift * (1.0 - p) y:center - unit * 1.05 * (1.0 - p) unit:unit
                   depth:1.0 - p alpha:pow(p, 0.52) context:ctx];
    }

    _isAnimating = anyAnimating;
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
