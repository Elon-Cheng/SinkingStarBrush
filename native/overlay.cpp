#include "version.g.h"
#include <windows.h>
#include <d3d12.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <filesystem>
#include <fstream>
#include <vector>
#include <mutex>
#include <atomic>
#include <algorithm>
#include <cmath>
#include <stdexcept>
#include <string>
#include "input-policy.h"
#include "imgui.h"
#include "imgui_impl_win32.h"
#include "imgui_impl_dx12.h"
using Microsoft::WRL::ComPtr;
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND, UINT, WPARAM, LPARAM);

// Native D3D12 in-process overlay. No process suspension or game-memory offsets.
namespace {
std::recursive_mutex gate;
HMODULE realDxgi;
HWND gameWindow;
WNDPROC oldProc;
ImGuiContext* context;
std::atomic_bool drawing{false};
std::atomic_bool settingsOpen{false};
std::atomic_bool capturedKeys[256]{};
bool pluginEnabled = true;
std::string settingsNotice;
bool visible = true, ready = false, failed = false, strokeActive = false;
int tool = 0, sequence = 1;
float thickness = 3.5f;
ImVec4 ink(1.f, .78f, .32f, 1.f);
std::string notice;
ImFont* headingFont;
const ImVec4 parchment(1.f,.92f,.62f,1.f), gold(1.f,.67f,.30f,1.f);
void ApplyGameStyle() {
    auto& s=ImGui::GetStyle();
    s.WindowPadding={20,16};s.FramePadding={10,7};s.ItemSpacing={9,10};s.ItemInnerSpacing={8,6};
    s.WindowRounding=3;s.FrameRounding=1;s.PopupRounding=2;s.GrabRounding=1;
    s.WindowBorderSize=0;s.FrameBorderSize=0;s.PopupBorderSize=1;s.ScrollbarSize=10;
    auto c=s.Colors;
    c[ImGuiCol_Text]=gold;c[ImGuiCol_TextDisabled]={.73f,.78f,.72f,.85f};
    c[ImGuiCol_WindowBg]={.035f,.17f,.22f,.86f};c[ImGuiCol_PopupBg]={.04f,.16f,.21f,.97f};
    c[ImGuiCol_Border]={.66f,.57f,.36f,.55f};c[ImGuiCol_BorderShadow]={0,0,0,0};
    c[ImGuiCol_FrameBg]={.33f,.31f,.23f,.35f};c[ImGuiCol_FrameBgHovered]={.48f,.41f,.25f,.5f};c[ImGuiCol_FrameBgActive]={.58f,.47f,.24f,.65f};
    c[ImGuiCol_Button]={0,0,0,0};c[ImGuiCol_ButtonHovered]={.75f,.68f,.40f,.12f};c[ImGuiCol_ButtonActive]={.75f,.68f,.40f,.20f};
    c[ImGuiCol_Header]={.53f,.43f,.22f,.4f};c[ImGuiCol_HeaderHovered]={.56f,.46f,.26f,.5f};c[ImGuiCol_HeaderActive]={.65f,.52f,.27f,.65f};
    c[ImGuiCol_TitleBg]={.035f,.14f,.18f,.96f};c[ImGuiCol_TitleBgActive]={.07f,.22f,.26f,.98f};
    c[ImGuiCol_CheckMark]=gold;c[ImGuiCol_SliderGrab]=gold;c[ImGuiCol_SliderGrabActive]=parchment;
    c[ImGuiCol_Separator]={.66f,.57f,.36f,.35f};c[ImGuiCol_SeparatorHovered]=gold;c[ImGuiCol_SeparatorActive]=gold;
    c[ImGuiCol_NavCursor]=gold;c[ImGuiCol_TextSelectedBg]={.65f,.52f,.27f,.4f};
}
void Rule(float width) {
    auto p=ImGui::GetCursorScreenPos();auto d=ImGui::GetWindowDrawList();auto color=IM_COL32(173,151,96,105);
    d->AddLine({p.x,p.y+4},{p.x+width,p.y+4},color);
    d->AddQuadFilled({p.x+width/2-3,p.y+4},{p.x+width/2,p.y+1},{p.x+width/2+3,p.y+4},{p.x+width/2,p.y+7},IM_COL32(195,170,110,180));
    ImGui::Dummy({width,9});
}
struct Stroke { int tool = 0, number = 0; ImU32 color = 0; float width = 3; std::vector<ImVec2> points; };
std::vector<Stroke> strokes;
std::vector<std::vector<Stroke>> undoStack, redoStack;
ComPtr<IDXGISwapChain3> chain;
ComPtr<ID3D12Device> device;
ComPtr<ID3D12CommandQueue> queue;
ComPtr<ID3D12DescriptorHeap> rtvHeap, srvHeap;
ComPtr<ID3D12GraphicsCommandList> commands;
ComPtr<ID3D12Fence> fence;
UINT64 fenceValue = 0;
HANDLE fenceEvent;
struct Frame { ComPtr<ID3D12Resource> buffer; ComPtr<ID3D12CommandAllocator> allocator; D3D12_CPU_DESCRIPTOR_HANDLE rtv{}; UINT64 fence = 0; };
std::vector<Frame> frames;
using CreateOld = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory*, IUnknown*, DXGI_SWAP_CHAIN_DESC*, IDXGISwapChain**);
using CreateHwnd = HRESULT(STDMETHODCALLTYPE*)(IDXGIFactory2*, IUnknown*, HWND, const DXGI_SWAP_CHAIN_DESC1*, const DXGI_SWAP_CHAIN_FULLSCREEN_DESC*, IDXGIOutput*, IDXGISwapChain1**);
using Present = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using Present1 = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);
using Resize = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT, UINT, DXGI_FORMAT, UINT);
using Resize1 = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain3*, UINT, UINT, UINT, DXGI_FORMAT, UINT, const UINT*, IUnknown* const*);
CreateOld originalCreate;
CreateHwnd originalCreateHwnd;
Present originalPresent;
Present1 originalPresent1;
Resize originalResize;
Resize1 originalResize1;

std::filesystem::path DataPath() {
    wchar_t local[MAX_PATH]{}; GetEnvironmentVariableW(L"LOCALAPPDATA", local, MAX_PATH);
    auto p = std::filesystem::path(local) / L"SinkingStarAssistant" / L"native";
    std::filesystem::create_directories(p); return p;
}
void Log(const char* text) noexcept { try { std::ofstream(DataPath() / "overlay.log", std::ios::app) << GetTickCount64() << " " << text << '\n'; } catch (...) {} }
void LoadConfig() {
    std::ifstream file(DataPath()/"plugin-settings.ini");
    std::string line;while(std::getline(file,line)){
        if(!line.empty()&&line.back()=='\r')line.pop_back();
        if(line=="enabled=0")pluginEnabled=false;
        if(line=="enabled=1")pluginEnabled=true;
    }
    Log(pluginEnabled?"Painter enabled from config":"Painter disabled from config");
}
void SaveConfig(bool enabled) {
    auto path=DataPath()/"plugin-settings.ini";auto temp=path;temp+=L".tmp";
    std::ofstream file(temp,std::ios::binary);file<<"[Painter]\nenabled="<<(enabled?1:0)<<"\n";file.close();
    if(!file||!MoveFileExW(temp.c_str(),path.c_str(),MOVEFILE_REPLACE_EXISTING|MOVEFILE_WRITE_THROUGH))throw std::runtime_error("Cannot save plugin settings");
}
void Check(HRESULT hr) { if (FAILED(hr)) { char msg[80]; sprintf_s(msg, "DirectX error 0x%08X", (unsigned)hr); throw std::runtime_error(msg); } }
template<class T> void Patch(void* object, int slot, T hook, T& original) {
    auto table = *reinterpret_cast<void***>(object);
    if (table[slot] == reinterpret_cast<void*>(hook)) return;
    DWORD protect;
    if (!VirtualProtect(&table[slot], sizeof(void*), PAGE_READWRITE, &protect)) throw std::runtime_error("Cannot patch virtual method");
    original = reinterpret_cast<T>(table[slot]);
    InterlockedExchangePointer(&table[slot], reinterpret_cast<void*>(hook));
    VirtualProtect(&table[slot], sizeof(void*), protect, &protect);
}
void Checkpoint() { if (undoStack.size() >= 50) undoStack.erase(undoStack.begin()); undoStack.push_back(strokes); redoStack.clear(); }
void Undo() { if (!undoStack.empty()) { redoStack.push_back(strokes); strokes = std::move(undoStack.back()); undoStack.pop_back(); strokeActive = false; } }
void Redo() { if (!redoStack.empty()) { undoStack.push_back(strokes); strokes = std::move(redoStack.back()); redoStack.pop_back(); strokeActive = false; } }
void SetDrawing(bool value) {
    if(value&&!pluginEnabled)return;
    drawing = value; strokeActive = false;
    if (value) {
        visible = true; ReleaseCapture(); ClipCursor(nullptr);
        // Keep held movement keys down: drawing only takes over the mouse.
    }
    if (context) ImGui::GetIO().MouseDrawCursor = value;
}
void SetSettings(bool value) {
    SetDrawing(false);settingsOpen=value;
    if(value){ReleaseCapture();ClipCursor(nullptr);
        if(oldProc)for(int k=8;k<256;++k)if(GetAsyncKeyState(k)&0x8000)CallWindowProcW(oldProc,gameWindow,WM_KEYUP,k,0xC0000001);
    }
    if(context)ImGui::GetIO().MouseDrawCursor=value;
}
bool CapturesKey(int key) {
    return (key >= 0 && key < 256 && capturedKeys[key]) ||
        PainterCapturesKey(key, drawing, settingsOpen, (GetAsyncKeyState(VK_CONTROL) & 0x8000) != 0);
}
SHORT WINAPI FilterKey(int key) { return CapturesKey(key) ? 0 : GetAsyncKeyState(key); }
void FilterPolling() {
    auto base = reinterpret_cast<BYTE*>(GetModuleHandleW(nullptr));
    auto dos = reinterpret_cast<IMAGE_DOS_HEADER*>(base);
    auto nt = reinterpret_cast<IMAGE_NT_HEADERS*>(base + dos->e_lfanew);
    auto rva = nt->OptionalHeader.DataDirectory[IMAGE_DIRECTORY_ENTRY_IMPORT].VirtualAddress;
    if (!rva) return;
    for (auto entry = reinterpret_cast<IMAGE_IMPORT_DESCRIPTOR*>(base + rva); entry->Name; ++entry) {
        if (!entry->OriginalFirstThunk) continue;
        auto names = reinterpret_cast<IMAGE_THUNK_DATA*>(base + entry->OriginalFirstThunk);
        auto addresses = reinterpret_cast<IMAGE_THUNK_DATA*>(base + entry->FirstThunk);
        for (; names->u1.AddressOfData; ++names, ++addresses) {
            if (IMAGE_SNAP_BY_ORDINAL(names->u1.Ordinal)) continue;
            auto name = reinterpret_cast<IMAGE_IMPORT_BY_NAME*>(base + names->u1.AddressOfData)->Name;
            if (strcmp(reinterpret_cast<char*>(name), "GetAsyncKeyState") == 0) {
                DWORD old; if (VirtualProtect(&addresses->u1.Function, sizeof(void*), PAGE_READWRITE, &old)) {
                    addresses->u1.Function = reinterpret_cast<ULONG_PTR>(&FilterKey);
                    VirtualProtect(&addresses->u1.Function, sizeof(void*), old, &old); Log("Game keyboard polling filtered");
                }
            }
        }
    }
}
LRESULT CALLBACK WindowProc(HWND hwnd, UINT msg, WPARAM w, LPARAM l) {
    std::lock_guard<std::recursive_mutex> lock(gate);
    if (context) ImGui::SetCurrentContext(context);
    if (msg == WM_ACTIVATEAPP && !w) {
        SetSettings(false);
        for (auto& key : capturedKeys) key = false;
    }
    const bool keyDown = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
    const bool keyUp = msg == WM_KEYUP || msg == WM_SYSKEYUP;
    // Remember consumed shortcuts until release, including after Esc/F8 closes drawing.
    const bool captured = (keyDown || keyUp) && w < 256 &&
        (CapturesKey((int)w) || w == VK_F8 || (pluginEnabled && w == VK_F9));
    if (keyDown && captured) capturedKeys[w] = true;
    if (keyUp && w < 256) capturedKeys[w] = false;
    if (msg == WM_KEYDOWN && !(l & (1LL << 30))) {
        if (w == VK_ESCAPE && settingsOpen) { SetSettings(false); return 0; }
        if (w == VK_F8) { if(!pluginEnabled){SetSettings(!settingsOpen);return 0;} bool next=!drawing;SetSettings(false);SetDrawing(next); return 0; }
        if (w == VK_F9 && pluginEnabled) { visible = !visible; if (!visible) SetDrawing(false); return 0; }
        if (w == VK_ESCAPE && drawing) { SetDrawing(false); return 0; }
        if (drawing && (GetAsyncKeyState(VK_CONTROL) & 0x8000)) { if (w == 'Z') { Undo(); return 0; } if (w == 'Y') { Redo(); return 0; } }
    }
    if (context && ready) ImGui_ImplWin32_WndProcHandler(hwnd, msg, w, l);
    if (captured) {
        if (msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP) return DefWindowProcW(hwnd, msg, w, l);
        return 0;
    }
    if (drawing||settingsOpen) {
        if (msg == WM_SETCURSOR) { SetCursor(LoadCursorW(nullptr, IDC_CROSS)); return TRUE; }
        if (msg == WM_INPUT) {
            RAWINPUT input{}; UINT bytes = sizeof(input);
            const UINT read = GetRawInputData(reinterpret_cast<HRAWINPUT>(l), RID_INPUT, &input, &bytes, sizeof(RAWINPUTHEADER));
            if (settingsOpen || read == (UINT)-1 || input.header.dwType == RIM_TYPEMOUSE ||
                (input.header.dwType == RIM_TYPEKEYBOARD && CapturesKey(input.data.keyboard.VKey)))
                return DefWindowProcW(hwnd, msg, w, l);
        }
        if ((msg >= WM_MOUSEFIRST && msg <= WM_MOUSELAST) || ((settingsOpen) && msg >= WM_KEYFIRST && msg <= WM_KEYLAST)) {
            // Allow Alt+Tab/Alt+F4 through the OS, never trap application switching.
            if (msg == WM_SYSKEYDOWN || msg == WM_SYSKEYUP) return DefWindowProcW(hwnd, msg, w, l);
            return 0;
        }
    }
    return CallWindowProcW(oldProc, hwnd, msg, w, l);
}
void SaveMarks() {
    auto path = DataPath() / "last-drawing.ssp"; auto temp = path; temp += L".tmp";
    std::ofstream file(temp); if (!file) throw std::runtime_error("Cannot save drawing");
    file << "SSP1 " << strokes.size() << '\n';
    for (const auto& s : strokes) { file << s.tool << ' ' << s.number << ' ' << s.color << ' ' << s.width << ' ' << s.points.size() << '\n'; for (auto p : s.points) file << p.x << ' ' << p.y << '\n'; }
    file.close(); if (!file || !MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) throw std::runtime_error("Drawing save failed");
    notice = "标注已保存到本机（屏幕坐标）";
}
void LoadMarks() {
    std::ifstream file(DataPath() / "last-drawing.ssp"); std::string magic; size_t count;
    if (!(file >> magic >> count) || magic != "SSP1" || count > 10000) { notice = "没有有效的已保存标注"; return; }
    std::vector<Stroke> loaded;
    for (size_t i = 0; i < count; ++i) {
        Stroke s; size_t n;
        if (!(file >> s.tool >> s.number >> s.color >> s.width >> n) || n > 100000 || s.tool < 0 || s.tool > 5 || !std::isfinite(s.width) || s.width < 1 || s.width > 30) throw std::runtime_error("Invalid drawing file");
        for (size_t j = 0; j < n; ++j) { ImVec2 p; if (!(file >> p.x >> p.y) || !std::isfinite(p.x) || !std::isfinite(p.y) || p.x < 0 || p.x > 1 || p.y < 0 || p.y > 1) throw std::runtime_error("Invalid point"); s.points.push_back(p); }
        loaded.push_back(std::move(s));
    }
    Checkpoint(); strokes = std::move(loaded); sequence = 1; for (const auto& s : strokes) sequence = std::max(sequence, s.number + 1); notice = "标注已恢复";
}
float Distance(ImVec2 p, ImVec2 a, ImVec2 b) { float x = b.x-a.x, y=b.y-a.y, d=x*x+y*y; float t=d>0?std::clamp(((p.x-a.x)*x+(p.y-a.y)*y)/d,0.f,1.f):0; return std::hypot(p.x-a.x-t*x,p.y-a.y-t*y); }
ImVec2 Pixel(ImVec2 p, ImVec2 size) { return {p.x*size.x,p.y*size.y}; }
void Erase(ImVec2 mouse, ImVec2 size) {
    for (int i = (int)strokes.size()-1; i >= 0; --i) {
        const auto& s=strokes[i]; if (s.points.empty()) continue;
        auto a=Pixel(s.points.front(),size), b=Pixel(s.points.back(),size); bool hit=false; float radius=12+s.width;
        if(s.tool==5) hit=std::hypot(mouse.x-a.x,mouse.y-a.y)<22;
        else if(s.tool==3) { ImVec2 c{b.x,a.y},d{a.x,b.y}; hit=std::min({Distance(mouse,a,c),Distance(mouse,c,b),Distance(mouse,b,d),Distance(mouse,d,a)})<radius; }
        else if(s.tool==4) { ImVec2 center{(a.x+b.x)/2,(a.y+b.y)/2}; float rx=fabsf(a.x-b.x)/2,ry=fabsf(a.y-b.y)/2; if(rx>1 && ry>1) hit=fabsf(std::hypot((mouse.x-center.x)/rx,(mouse.y-center.y)/ry)-1)*std::min(rx,ry)<radius; }
        else { if(s.points.size()==1) hit=Distance(mouse,a,a)<radius; for(size_t j=1;j<s.points.size();++j) if(Distance(mouse,Pixel(s.points[j-1],size),Pixel(s.points[j],size))<radius) hit=true; }
        if(hit) { strokes.erase(strokes.begin()+i); break; }
    }
}
// Small vector icons use the same ink as the menu; no icon font or emoji dependency.
void ToolIcon(int icon,ImVec2 origin,ImU32 color) {
    auto d=ImGui::GetWindowDrawList();
    auto p=[&](float x,float y){return ImVec2(origin.x+x,origin.y+y);};
    auto line=[&](float x,float y,float a,float b){d->AddLine(p(x,y),p(a,b),color,1.4f);};
    switch(icon){
    case 0: line(2,12,11,3);line(4,14,13,5);line(11,3,13,5);line(2,12,1,15);line(1,15,4,14);line(9,5,11,7);break;
    case 1: line(2,13,13,2);d->AddCircleFilled(p(2,13),1.7f,color);d->AddCircleFilled(p(13,2),1.7f,color);break;
    case 2: line(2,13,13,2);line(6,2,13,2);line(13,2,13,9);break;
    case 3: d->AddRect(p(2,3),p(14,13),color,0,0,1.4f);break;
    case 4: d->AddCircle(p(8,8),6,color,20,1.4f);break;
    case 5: d->AddCircle(p(8,8),7,color,20,1.2f);line(6,6,8,4);line(8,4,8,12);line(5,12,11,12);break;
    case 6: line(2,10,9,3);line(9,3,14,8);line(14,8,8,14);line(8,14,5,14);line(5,14,2,10);line(5,7,11,12);break;
    case 7: d->AddCircle(p(8,8),6.5f,color,20,1.3f);d->AddCircleFilled(p(5,5),1.5f,color);d->AddCircleFilled(p(10,4),1.5f,color);d->AddCircleFilled(p(12,9),1.5f,color);line(4,12,8,12);break;
    case 8: case 9:{float sign=icon==8?1.f:-1.f;auto q=[&](float x,float y){return p(icon==8?x:16-x,y);};d->AddBezierCubic(q(3,6),q(14,1),q(17,14),q(7,13),color,1.4f);d->AddLine(q(3,6),q(7,2),color,1.4f);d->AddLine(q(3,6),q(8,8),color,1.4f);(void)sign;break;}
    case 10:line(2,4,14,4);line(6,2,10,2);line(4,4,5,14);line(5,14,11,14);line(11,14,12,4);line(7,7,7,11);line(9,7,9,11);break;
    case 11:d->AddRect(p(2,2),p(14,14),color,0,0,1.3f);d->AddRect(p(5,2),p(11,6),color);d->AddRect(p(5,10),p(11,14),color);break;
    case 12:line(2,3,10,3);line(2,3,2,13);line(2,13,14,13);line(14,13,14,9);line(8,9,8,1);line(5,6,8,9);line(11,6,8,9);break;
    case 13:line(2,8,6,12);line(6,12,14,3);break;
    case 14:d->AddCircle(p(8,8),5,color,16,1.4f);d->AddCircle(p(8,8),2,color,12,1.3f);for(int i=0;i<8;++i){float a=i*3.14159265f/4;line(8+5*cosf(a),8+5*sinf(a),8+8*cosf(a),8+8*sinf(a));}break;
    }
}
void DrawSettings() {
    if(!settingsOpen)return;
    auto size=ImGui::GetIO().DisplaySize;
    ImGui::SetNextWindowPos({size.x*.5f,size.y*.5f},ImGuiCond_Appearing,{.5f,.5f});
    ImGui::SetNextWindowSize({400,0});
    ImGui::Begin("插件设置###PainterSettings",nullptr,ImGuiWindowFlags_AlwaysAutoResize|ImGuiWindowFlags_NoCollapse);
    ImGui::TextColored(parchment,"沉星画笔   v" PAINTER_VERSION);Rule(350);
    bool next=pluginEnabled;
    if(ImGui::Checkbox("启用画笔插件",&next)){
        try{SaveConfig(next);pluginEnabled=next;SetDrawing(false);ImGui::GetIO().MouseDrawCursor=true;
            if(next)visible=true;settingsNotice=next?"已启用并保存":"已关闭并保存";
            Log(next?"Painter enabled in settings":"Painter disabled in settings");
        }catch(const std::exception& e){Log(e.what());settingsNotice="保存失败，开关未改变。请检查日志。";}
    }
    ImGui::TextWrapped("关闭后，画笔工具栏、笔迹和 F9 功能停用。已保存的笔迹会保留。");
    ImGui::Spacing();ImGui::TextColored(parchment,"关闭插件后，可按 F8 重新启用");
    ImGui::TextDisabled("开关立即生效，重启游戏后保留");
    if(!settingsNotice.empty())ImGui::TextWrapped("%s",settingsNotice.c_str());
    Rule(350);
    if(ImGui::Button("返回游戏",{350,36}))SetSettings(false);
    ImGui::TextDisabled("F8 画笔  ·  F9 显隐  ·  Esc 关闭此面板");
    ImGui::End();
}
void DrawUi() {
    auto& io=ImGui::GetIO(); auto size=io.DisplaySize;
    DrawSettings();
    if(!pluginEnabled||settingsOpen)return;
    if(!visible || size.x<=0 || size.y<=0) return;
    auto dl=ImGui::GetBackgroundDrawList();
    for(const auto& s:strokes) {
        if(s.points.empty()) continue;
        auto a=Pixel(s.points.front(),size), b=Pixel(s.points.back(),size);
        if(s.tool==3) dl->AddRect({std::min(a.x,b.x),std::min(a.y,b.y)},{std::max(a.x,b.x),std::max(a.y,b.y)},s.color,0,0,s.width);
        else if(s.tool==4) dl->AddEllipse({(a.x+b.x)/2,(a.y+b.y)/2},{fabsf(a.x-b.x)/2,fabsf(a.y-b.y)/2},s.color,0,0,s.width);
        else if(s.tool==5) { dl->AddCircleFilled(a,17,IM_COL32(15,23,38,235)); dl->AddCircle(a,17,s.color,0,2); char label[20]; sprintf_s(label,"%d",s.number); auto t=ImGui::CalcTextSize(label);dl->AddText({a.x-t.x/2,a.y-t.y/2},s.color,label); }
        else {
            if(s.points.size()==1) dl->AddCircleFilled(a,s.width/2,s.color);
            for(size_t j=1;j<s.points.size();++j) dl->AddLine(Pixel(s.points[j-1],size),Pixel(s.points[j],size),s.color,s.width);
            if(s.tool==2) { float x=b.x-a.x,y=b.y-a.y,len=std::hypot(x,y); if(len>1){x/=len;y/=len;float n=12+s.width*2;dl->AddLine(b,{b.x-x*n-y*n*.5f,b.y-y*n+x*n*.5f},s.color,s.width);dl->AddLine(b,{b.x-x*n+y*n*.5f,b.y-y*n-x*n*.5f},s.color,s.width);} }
        }
    }
    // Fixed left rail. Only transient settings open to its right.
    const float railWidth=76.f;
    const float railHeight=drawing?std::min(650.f,std::max(120.f,size.y-24.f)):80.f;
    ImGui::SetNextWindowPos({0,12},ImGuiCond_Always);
    ImGui::SetNextWindowSize({railWidth,railHeight},ImGuiCond_Always);
    ImGui::SetNextWindowBgAlpha(drawing?.86f:.48f);
    ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,{8,10});
    ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing,{0,3});
    ImGui::PushStyleVar(ImGuiStyleVar_FramePadding,{4,5});
    ImGui::Begin("画笔###StarPainter",nullptr,ImGuiWindowFlags_NoTitleBar|ImGuiWindowFlags_NoCollapse|ImGuiWindowFlags_NoMove|ImGuiWindowFlags_NoResize|(drawing?0:ImGuiWindowFlags_NoInputs));
    auto railButton=[](const char* label,const char* hint,int icon){
        ImGui::PushID(label);
        bool pressed=ImGui::Button("##tool",{60,28});
        auto a=ImGui::GetItemRectMin();auto color=ImGui::GetColorU32(ImGuiCol_Text);
        ToolIcon(icon,{a.x+2,a.y+6},color);
        ImGui::GetWindowDrawList()->AddText(ImGui::GetFont(),18,{a.x+22,a.y+5},color,label);
        if(ImGui::IsItemHovered(ImGuiHoveredFlags_DelayNormal))ImGui::SetTooltip("%s",hint);
        ImGui::PopID();
        return pressed;
    };
    if(!drawing) {
        auto p=ImGui::GetCursorScreenPos();ToolIcon(0,{p.x,p.y+3},ImGui::GetColorU32(parchment));
        ImGui::GetWindowDrawList()->AddText(ImGui::GetFont(),18,{p.x+22,p.y+2},ImGui::GetColorU32(parchment),"画笔");ImGui::Dummy({60,24});
        ImGui::TextDisabled("F8");
    } else {
        ImGui::TextColored(parchment,"画笔");
        Rule(60);
        const char* names[]={"画笔","直线","箭头","矩形","圆圈","编号","橡皮"};
        for(int i=0;i<7;++i){ImGui::PushID(i);
            ImGui::PushStyleColor(ImGuiCol_Text,tool==i?parchment:gold);
            if(railButton(names[i],names[i],i))tool=i;
            if(tool==i){auto a=ImGui::GetItemRectMin(),b=ImGui::GetItemRectMax();ImGui::GetWindowDrawList()->AddLine({a.x-4,a.y+6},{a.x-4,b.y-6},IM_COL32(255,232,158,210),2);}
            ImGui::PopStyleColor();ImGui::PopID();}
        Rule(60);
        if(railButton("墨色","设置颜色和笔触",7))ImGui::OpenPopup("墨色与笔触");
        ImGui::SetNextWindowPos({railWidth+8,std::clamp(ImGui::GetItemRectMin().y,12.f,std::max(12.f,size.y-180.f))},ImGuiCond_Appearing);
        ImGui::PushStyleVar(ImGuiStyleVar_WindowPadding,{16,14});
        ImGui::PushStyleVar(ImGuiStyleVar_ItemSpacing,{8,10});
        if(ImGui::BeginPopup("墨色与笔触")){
            ImGui::TextColored(parchment,"墨色与笔触");
            const ImVec4 colors[]={{1,.78f,.32f,1},{.96f,.91f,.73f,1},{.88f,.37f,.28f,1},{.40f,.78f,.70f,1},{.55f,.67f,.90f,1}};
            for(int i=0;i<5;++i){if(i)ImGui::SameLine();ImGui::PushID(i+20);auto p=ImGui::GetCursorScreenPos();
                if(ImGui::InvisibleButton("ink",{28,28}))ink=colors[i];
                auto d=ImGui::GetWindowDrawList();d->AddCircleFilled({p.x+14,p.y+14},8,ImGui::ColorConvertFloat4ToU32(colors[i]));
                if(fabsf(ink.x-colors[i].x)<.01f&&fabsf(ink.y-colors[i].y)<.01f)d->AddCircle({p.x+14,p.y+14},12,IM_COL32(255,232,158,220),0,1.2f);
                ImGui::PopID();}
            ImGui::SetNextItemWidth(155);ImGui::SliderFloat("笔触",&thickness,1,12,"%.1f");
            if(ImGui::Button("完成",{210,30}))ImGui::CloseCurrentPopup();
            ImGui::EndPopup();
        }
        ImGui::PopStyleVar(2);
        ImGui::BeginDisabled(undoStack.empty());if(railButton("撤销","Ctrl+Z",8))Undo();ImGui::EndDisabled();
        ImGui::BeginDisabled(redoStack.empty());if(railButton("重做","Ctrl+Y",9))Redo();ImGui::EndDisabled();
        if(railButton("清空","清空笔迹，可撤销",10)){Checkpoint();strokes.clear();sequence=1;}
        Rule(60);
        if(railButton("保存","保存当前笔迹",11))SaveMarks();
        if(railButton("恢复","恢复上次保存的笔迹",12))LoadMarks();
        if(railButton("设置","插件开关",14))SetSettings(true);
        if(!notice.empty()&&ImGui::IsWindowHovered()&&!ImGui::IsAnyItemHovered())ImGui::SetTooltip("%s",notice.c_str());
        Rule(60);
        ImGui::PushStyleColor(ImGuiCol_Text,parchment);
        if(railButton("完成","F8 / Esc 返回游戏，F9 隐藏标注",13))SetDrawing(false);
        ImGui::PopStyleColor();
    }
    bool over=ImGui::IsWindowHovered(ImGuiHoveredFlags_AnyWindow)|ImGui::IsPopupOpen(nullptr,ImGuiPopupFlags_AnyPopupId);
    // A click consumed by a button or dismissing a popup must never start a stroke.
    over=over||ImGui::GetIO().WantCaptureMouse;
    ImGui::End();ImGui::PopStyleVar(3);
    if(!drawing) return;
    ImVec2 point{std::clamp(io.MousePos.x/size.x,0.f,1.f),std::clamp(io.MousePos.y/size.y,0.f,1.f)};
    if(ImGui::IsMouseClicked(0)&&!over){Checkpoint();strokeActive=true;if(tool!=6){Stroke s;s.tool=tool;s.width=thickness;s.color=ImGui::ColorConvertFloat4ToU32(ink);s.number=tool==5?sequence++:0;s.points.push_back(point);strokes.push_back(std::move(s));}}
    if(strokeActive&&ImGui::IsMouseDown(0)) {
        if(tool==6)Erase(io.MousePos,size);
        else if(!strokes.empty()&&strokes.back().tool!=5){auto& p=strokes.back().points;if(tool==0){if(Distance(io.MousePos,Pixel(p.back(),size),Pixel(p.back(),size))>1.5f)p.push_back(point);}else if(p.size()==1)p.push_back(point);else p.back()=point;}
    }
    if(ImGui::IsMouseReleased(0))strokeActive=false;
}
void Wait(UINT64 value) {
    if(!value || fence->GetCompletedValue()>=value)return;
    Check(fence->SetEventOnCompletion(value,fenceEvent));
    if(WaitForSingleObject(fenceEvent,3000)!=WAIT_OBJECT_0)throw std::runtime_error("Overlay GPU fence timeout");
}
void ReleaseBuffers() { for(auto& f:frames){Wait(f.fence);f.buffer.Reset();} }
void Init() {
    Check(chain->GetDevice(IID_PPV_ARGS(&device)));DXGI_SWAP_CHAIN_DESC desc;Check(chain->GetDesc(&desc));gameWindow=desc.OutputWindow;
    D3D12_DESCRIPTOR_HEAP_DESC heap{};heap.NumDescriptors=desc.BufferCount;heap.Type=D3D12_DESCRIPTOR_HEAP_TYPE_RTV;Check(device->CreateDescriptorHeap(&heap,IID_PPV_ARGS(&rtvHeap)));
    heap.NumDescriptors=1;heap.Type=D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;heap.Flags=D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;Check(device->CreateDescriptorHeap(&heap,IID_PPV_ARGS(&srvHeap)));
    frames.resize(desc.BufferCount);auto rtv=rtvHeap->GetCPUDescriptorHandleForHeapStart();auto step=device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);
    for(UINT i=0;i<desc.BufferCount;++i){auto& f=frames[i];Check(chain->GetBuffer(i,IID_PPV_ARGS(&f.buffer)));Check(device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT,IID_PPV_ARGS(&f.allocator)));f.rtv=rtv;device->CreateRenderTargetView(f.buffer.Get(),nullptr,rtv);rtv.ptr+=step;}
    Check(device->CreateCommandList(0,D3D12_COMMAND_LIST_TYPE_DIRECT,frames[0].allocator.Get(),nullptr,IID_PPV_ARGS(&commands)));Check(commands->Close());
    Check(device->CreateFence(0,D3D12_FENCE_FLAG_NONE,IID_PPV_ARGS(&fence)));fenceEvent=CreateEventW(nullptr,FALSE,FALSE,nullptr);if(!fenceEvent)throw std::runtime_error("Cannot create fence event");
    if(!context){context=ImGui::CreateContext();auto& io=ImGui::GetIO();io.IniFilename=nullptr;io.LogFilename=nullptr;io.ConfigFlags|=ImGuiConfigFlags_NoMouseCursorChange;
        wchar_t exe[MAX_PATH];GetModuleFileNameW(nullptr,exe,MAX_PATH);
        auto font=std::filesystem::path(exe).parent_path()/"data"/"fonts"/"SourceHanSerifCN-SemiBold.otf";
        if(!std::filesystem::exists(font)){wchar_t windows[MAX_PATH];GetWindowsDirectoryW(windows,MAX_PATH);font=std::filesystem::path(windows)/"Fonts"/"simsun.ttc";Log("Game font missing: using system serif");}
        // Read through a wide-path stream: game directories may contain Chinese characters.
        std::ifstream fontFile(font,std::ios::binary|std::ios::ate);auto bytes=fontFile.tellg();
        if(bytes<=0||bytes>64*1024*1024)throw std::runtime_error("Cannot read UI font");
        std::vector<char> fontData((size_t)bytes);fontFile.seekg(0);fontFile.read(fontData.data(),bytes);
        ImFontConfig config;config.FontDataOwnedByAtlas=true;
        for(float size:{21.f,29.f}){void* copy=IM_ALLOC(fontData.size());memcpy(copy,fontData.data(),fontData.size());auto f=io.Fonts->AddFontFromMemoryTTF(copy,(int)fontData.size(),size,&config,io.Fonts->GetGlyphRangesChineseSimplifiedCommon());if(size==29)headingFont=f;}
        ApplyGameStyle();LoadConfig();Log("Game painter loaded v" PAINTER_VERSION "; toolbar settings available");
        if(!ImGui_ImplWin32_Init(gameWindow))throw std::runtime_error("Win32 backend failed");
        oldProc=reinterpret_cast<WNDPROC>(SetWindowLongPtrW(gameWindow,GWLP_WNDPROC,reinterpret_cast<LONG_PTR>(WindowProc)));if(!oldProc)throw std::runtime_error("Cannot attach input handler");FilterPolling();
    }
    ImGui_ImplDX12_InitInfo info;info.Device=device.Get();info.CommandQueue=queue.Get();info.NumFramesInFlight=(int)frames.size();info.RTVFormat=desc.BufferDesc.Format;info.SrvDescriptorHeap=srvHeap.Get();
    info.SrvDescriptorAllocFn=[](ImGui_ImplDX12_InitInfo* i,D3D12_CPU_DESCRIPTOR_HANDLE* cpu,D3D12_GPU_DESCRIPTOR_HANDLE* gpu){*cpu=i->SrvDescriptorHeap->GetCPUDescriptorHandleForHeapStart();*gpu=i->SrvDescriptorHeap->GetGPUDescriptorHandleForHeapStart();};info.SrvDescriptorFreeFn=[](ImGui_ImplDX12_InitInfo*,D3D12_CPU_DESCRIPTOR_HANDLE,D3D12_GPU_DESCRIPTOR_HANDLE){};
    if(!ImGui_ImplDX12_Init(&info))throw std::runtime_error("DX12 backend failed");ready=true;Log("In-game renderer ready");
}
void Render(IDXGISwapChain* current) noexcept {
    std::lock_guard<std::recursive_mutex> lock(gate);
    if(failed||!chain||current!=chain.Get())return;
    try{
        if(!ready)Init();ImGui::SetCurrentContext(context);
        if(GetForegroundWindow()!=gameWindow){SetSettings(false);return;}
        auto& f=frames[chain->GetCurrentBackBufferIndex()];Wait(f.fence);
        ImGui_ImplDX12_NewFrame();ImGui_ImplWin32_NewFrame();ImGui::NewFrame();DrawUi();ImGui::Render();
        Check(f.allocator->Reset());Check(commands->Reset(f.allocator.Get(),nullptr));
        D3D12_RESOURCE_BARRIER barrier{};barrier.Type=D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;barrier.Transition.pResource=f.buffer.Get();barrier.Transition.Subresource=D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;barrier.Transition.StateBefore=D3D12_RESOURCE_STATE_PRESENT;barrier.Transition.StateAfter=D3D12_RESOURCE_STATE_RENDER_TARGET;commands->ResourceBarrier(1,&barrier);
        commands->OMSetRenderTargets(1,&f.rtv,FALSE,nullptr);ID3D12DescriptorHeap* heaps[]={srvHeap.Get()};commands->SetDescriptorHeaps(1,heaps);ImGui_ImplDX12_RenderDrawData(ImGui::GetDrawData(),commands.Get());
        std::swap(barrier.Transition.StateBefore,barrier.Transition.StateAfter);commands->ResourceBarrier(1,&barrier);Check(commands->Close());ID3D12CommandList* lists[]={commands.Get()};queue->ExecuteCommandLists(1,lists);f.fence=++fenceValue;Check(queue->Signal(fence.Get(),f.fence));
    }catch(const std::exception& e){Log(e.what());SetDrawing(false);failed=true;}
}
void BeforeResize(IDXGISwapChain* current) {
    if(current!=chain.Get()||!ready)return;
    SetDrawing(false);ReleaseBuffers();ImGui_ImplDX12_Shutdown();ready=false;frames.clear();commands.Reset();rtvHeap.Reset();srvHeap.Reset();fence.Reset();CloseHandle(fenceEvent);fenceEvent=nullptr;fenceValue=0;Log("Swap chain resize: resources released");
}
HRESULT STDMETHODCALLTYPE OnPresent(IDXGISwapChain* s,UINT sync,UINT flags){if(!(flags&DXGI_PRESENT_TEST))Render(s);return originalPresent(s,sync,flags);}
HRESULT STDMETHODCALLTYPE OnPresent1(IDXGISwapChain1* s,UINT sync,UINT flags,const DXGI_PRESENT_PARAMETERS* p){if(!(flags&DXGI_PRESENT_TEST))Render(s);return originalPresent1(s,sync,flags,p);}
HRESULT STDMETHODCALLTYPE OnResize(IDXGISwapChain* s,UINT n,UINT w,UINT h,DXGI_FORMAT f,UINT flags){std::lock_guard<std::recursive_mutex> lock(gate);try{BeforeResize(s);}catch(const std::exception&e){Log(e.what());failed=true;}return originalResize(s,n,w,h,f,flags);}
HRESULT STDMETHODCALLTYPE OnResize1(IDXGISwapChain3* s,UINT n,UINT w,UINT h,DXGI_FORMAT f,UINT flags,const UINT* mask,IUnknown* const* queues){std::lock_guard<std::recursive_mutex> lock(gate);try{BeforeResize(s);}catch(const std::exception&e){Log(e.what());failed=true;}return originalResize1(s,n,w,h,f,flags,mask,queues);}
void Attach(IUnknown* q,IDXGISwapChain* s) {
    std::lock_guard<std::recursive_mutex> lock(gate);if(chain)return;
    ComPtr<ID3D12CommandQueue> found; if(FAILED(q->QueryInterface(IID_PPV_ARGS(&found))))return;
    if(found->GetDesc().Type!=D3D12_COMMAND_LIST_TYPE_DIRECT)return;
    Check(s->QueryInterface(IID_PPV_ARGS(&chain)));queue=found;
    Patch(chain.Get(),8,OnPresent,originalPresent);Patch(chain.Get(),13,OnResize,originalResize);Patch(chain.Get(),22,OnPresent1,originalPresent1);Patch(chain.Get(),39,OnResize1,originalResize1);Log("DX12 swap chain attached");
}
HRESULT STDMETHODCALLTYPE OnCreate(IDXGIFactory* f,IUnknown* q,DXGI_SWAP_CHAIN_DESC* d,IDXGISwapChain** out){auto hr=originalCreate(f,q,d,out);if(SUCCEEDED(hr))try{Attach(q,*out);}catch(const std::exception&e){Log(e.what());}return hr;}
HRESULT STDMETHODCALLTYPE OnCreateHwnd(IDXGIFactory2* f,IUnknown* q,HWND hwnd,const DXGI_SWAP_CHAIN_DESC1* d,const DXGI_SWAP_CHAIN_FULLSCREEN_DESC* full,IDXGIOutput* restrictOutput,IDXGISwapChain1** out){auto hr=originalCreateHwnd(f,q,hwnd,d,full,restrictOutput,out);if(SUCCEEDED(hr))try{Attach(q,*out);}catch(const std::exception&e){Log(e.what());}return hr;}
void AttachFactory(void* p){std::lock_guard<std::recursive_mutex> lock(gate);ComPtr<IDXGIFactory2> f;if(SUCCEEDED(static_cast<IUnknown*>(p)->QueryInterface(IID_PPV_ARGS(&f)))){Patch< CreateOld >(f.Get(),10,OnCreate,originalCreate);Patch<CreateHwnd>(f.Get(),15,OnCreateHwnd,originalCreateHwnd);Log("DXGI factory attached");}}
FARPROC Real(const char* name){static std::once_flag once;std::call_once(once,[]{wchar_t path[MAX_PATH];GetSystemDirectoryW(path,MAX_PATH);wcscat_s(path,L"\\dxgi.dll");realDxgi=LoadLibraryW(path);Log("Native proxy loaded v" PAINTER_VERSION);});return realDxgi?GetProcAddress(realDxgi,name):nullptr;}
}
extern "C" HRESULT WINAPI ProxyCreateDXGIFactory(REFIID id,void** out){auto fn=reinterpret_cast<HRESULT(WINAPI*)(REFIID,void**)>(Real("CreateDXGIFactory"));if(!fn)return E_FAIL;auto hr=fn(id,out);if(SUCCEEDED(hr))try{AttachFactory(*out);}catch(...){Log("Factory attach failed");}return hr;}
extern "C" HRESULT WINAPI ProxyCreateDXGIFactory1(REFIID id,void** out){auto fn=reinterpret_cast<HRESULT(WINAPI*)(REFIID,void**)>(Real("CreateDXGIFactory1"));if(!fn)return E_FAIL;auto hr=fn(id,out);if(SUCCEEDED(hr))try{AttachFactory(*out);}catch(...){Log("Factory attach failed");}return hr;}
extern "C" HRESULT WINAPI ProxyCreateDXGIFactory2(UINT flags,REFIID id,void** out){auto fn=reinterpret_cast<HRESULT(WINAPI*)(UINT,REFIID,void**)>(Real("CreateDXGIFactory2"));if(!fn)return E_FAIL;auto hr=fn(flags,id,out);if(SUCCEEDED(hr))try{AttachFactory(*out);}catch(...){Log("Factory attach failed");}return hr;}
extern "C" HRESULT WINAPI ProxyDXGIGetDebugInterface1(UINT flags,REFIID id,void** out){auto fn=reinterpret_cast<HRESULT(WINAPI*)(UINT,REFIID,void**)>(Real("DXGIGetDebugInterface1"));return fn?fn(flags,id,out):E_NOINTERFACE;}
extern "C" HRESULT WINAPI ProxyDXGIDeclareAdapterRemovalSupport(){auto fn=reinterpret_cast<HRESULT(WINAPI*)()>(Real("DXGIDeclareAdapterRemovalSupport"));return fn?fn():S_OK;}
BOOL WINAPI DllMain(HINSTANCE instance,DWORD reason,LPVOID){if(reason==DLL_PROCESS_ATTACH)DisableThreadLibraryCalls(instance);return TRUE;}
