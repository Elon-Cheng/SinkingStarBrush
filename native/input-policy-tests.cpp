#include "input-policy.h"

// Movement works with modifiers and custom bindings, not just WASD.
static_assert(!PainterCapturesKey('W', true, false, false));
static_assert(!PainterCapturesKey('A', true, false, true));
static_assert(!PainterCapturesKey('S', true, false, false));
static_assert(!PainterCapturesKey('D', true, false, false));
static_assert(!PainterCapturesKey(VK_UP, true, false, false));
static_assert(!PainterCapturesKey(VK_LEFT, true, false, false));
static_assert(!PainterCapturesKey(VK_DOWN, true, false, false));
static_assert(!PainterCapturesKey(VK_RIGHT, true, false, false));
static_assert(!PainterCapturesKey(VK_SHIFT, true, false, false));
static_assert(!PainterCapturesKey(VK_SPACE, true, false, false));
static_assert(!PainterCapturesKey('Z', true, false, false));
static_assert(!PainterCapturesKey('Y', true, false, false));
static_assert(PainterCapturesKey('Z', true, false, true));
static_assert(PainterCapturesKey('Y', true, false, true));
static_assert(PainterCapturesKey(VK_ESCAPE, true, false, false));
static_assert(PainterCapturesKey(VK_F8, true, false, false));
static_assert(PainterCapturesKey(VK_F9, true, false, false));
static_assert(PainterCapturesKey(VK_LBUTTON, true, false, false));
static_assert(PainterCapturesKey(VK_RBUTTON, true, false, false));
static_assert(PainterCapturesKey('W', false, true, false));
static_assert(PainterCapturesKey(VK_LBUTTON, false, true, false));
static_assert(!PainterCapturesKey(VK_LBUTTON, false, false, false));
static_assert(!PainterCapturesKey('Z', false, false, true));
