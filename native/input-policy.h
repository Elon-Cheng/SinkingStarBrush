#pragma once
#include <windows.h>

// Shared by window messages, raw keyboard input and GetAsyncKeyState polling.
constexpr bool PainterCapturesKey(int key, bool drawing, bool settings, bool control) {
    if (settings) return true;
    if (!drawing) return false;
    return key == VK_LBUTTON || key == VK_RBUTTON || key == VK_MBUTTON ||
        key == VK_XBUTTON1 || key == VK_XBUTTON2 ||
        key == VK_F8 || key == VK_F9 || key == VK_ESCAPE ||
        (control && (key == 'Z' || key == 'Y'));
}
