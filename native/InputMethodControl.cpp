// A short-lived, thread-specific message hook runs TSF calls in the target UI thread.
// No keyboard hook, synthesized key, global hook or desktop-wide profile activation.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <msctf.h>
#include <wrl/client.h>
#include <cstdio>
#include <string>
#include <cstdint>
using Microsoft::WRL::ComPtr;

static const GUID StandaloneClass = {0x595b67e9,0x48a3,0x4c82,{0xb7,0xb1,0x64,0xe4,0xa3,0x5c,0x9d,0x92}};
static const GUID StandaloneProfile = {0x79c457d1,0x690a,0x4f83,{0xa3,0xde,0xc9,0x5c,0x98,0xe0,0x1d,0x4d}};
static const GUID XiaobaiClass = {0xa3f4cded,0xb1e9,0x41ee,{0x9c,0xa6,0x7b,0x4d,0x0d,0xe6,0xcb,0x0a}};
static const GUID XiaobaiProfile = {0x3d02cab6,0x2b8e,0x4781,{0xba,0x20,0x1c,0x92,0x67,0x52,0x94,0x67}};
static constexpr DWORD Magic = 0x49543947;
static constexpr wchar_t MessageName[] = L"GamePadT9.InputMethodControl.v1";
struct Profile {
    DWORD type = 0;
    WORD language = 0;
    GUID clsid{}, profile{};
    UINT_PTR keyboard = 0;
};
struct Request {
    DWORD magic, size, process, thread, operation;
    HWND window;
    Profile desired, before, after;
    HRESULT result;
    LONG complete;
};
static std::wstring MappingName(DWORD process, DWORD token) {
    return L"Local\\GamePadT9.InputMethod." + std::to_wstring(process) + L"." + std::to_wstring(token);
}
static bool Equal(const Profile& a, const Profile& b) {
    return a.type == b.type && a.language == b.language &&
        (a.type == TF_PROFILETYPE_INPUTPROCESSOR ? a.clsid == b.clsid && a.profile == b.profile : a.keyboard == b.keyboard);
}
static HRESULT Current(ITfInputProcessorProfileMgr* manager, Profile& out) {
    TF_INPUTPROCESSORPROFILE value{};
    auto hr = manager->GetActiveProfile(GUID_TFCAT_TIP_KEYBOARD, &value);
    if (hr != S_OK) return FAILED(hr) ? hr : E_FAIL;
    out = {value.dwProfileType, value.langid, value.clsid, value.guidProfile, reinterpret_cast<UINT_PTR>(value.hkl)};
    return out.type == TF_PROFILETYPE_INPUTPROCESSOR || (out.type == TF_PROFILETYPE_KEYBOARDLAYOUT && out.keyboard) ? S_OK : E_FAIL;
}
static HRESULT Activate(ITfInputProcessorProfileMgr* manager, const Profile& target) {
    ComPtr<ITfInputProcessorProfiles> languages;
    auto hr = manager->QueryInterface(IID_PPV_ARGS(&languages));
    LANGID current = 0;
    if (SUCCEEDED(hr)) hr = languages->GetCurrentLanguage(&current);
    if (SUCCEEDED(hr) && current != target.language) hr = languages->ChangeCurrentLanguage(target.language);
    if (hr != S_OK) return FAILED(hr) ? hr : E_FAIL;
    hr = manager->ActivateProfile(target.type, target.language, target.clsid, target.profile,
        reinterpret_cast<HKL>(target.keyboard), 0); // Current thread only.
    if (hr != S_OK) return FAILED(hr) ? hr : HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    Profile actual{};
    hr = Current(manager, actual);
    return hr == S_OK && Equal(actual, target) ? S_OK : FAILED(hr) ? hr : E_FAIL;
}
static bool Available(ITfInputProcessorProfileMgr* manager, const GUID& clsid, const GUID& profile, Profile& out) {
    TF_INPUTPROCESSORPROFILE value{};
    if (manager->GetProfile(TF_PROFILETYPE_INPUTPROCESSOR, 0x0804, clsid, profile, nullptr, &value) != S_OK ||
        !(value.dwFlags & TF_IPP_FLAG_ENABLED)) return false;
    out = {TF_PROFILETYPE_INPUTPROCESSOR, 0x0804, clsid, profile, 0};
    return true;
}
static HRESULT Execute(Request& request) {
    if (request.process != GetCurrentProcessId() || request.thread != GetCurrentThreadId()) return E_ACCESSDENIED;
    if ((request.operation == 1 || request.operation == 3) && GetAncestor(request.window, GA_ROOT) != GetForegroundWindow())
        return HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
    ComPtr<ITfInputProcessorProfileMgr> manager;
    auto hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&manager));
    if (FAILED(hr)) return hr;
    hr = Current(manager.Get(), request.before);
    if (FAILED(hr)) return hr; // Never switch if we cannot record what to restore.
    request.after = request.before;
    if (request.operation == 0) return S_OK;
    if ((request.operation == 1 || request.operation == 3) &&
        (request.operation == 3 || !Available(manager.Get(), StandaloneClass, StandaloneProfile, request.desired)) &&
        !Available(manager.Get(), XiaobaiClass, XiaobaiProfile, request.desired)) return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    if (Equal(request.before, request.desired)) return S_OK;
    hr = Activate(manager.Get(), request.desired);
    if (FAILED(hr)) Activate(manager.Get(), request.before); // Roll back a partial language change.
    Current(manager.Get(), request.after);
    return hr;
}

#ifndef INPUTMETHOD_HOST
extern "C" __declspec(dllexport) LRESULT CALLBACK InputMethodHook(int code, WPARAM wp, LPARAM lp) {
    static thread_local bool executing = false;
    if (code >= 0 && !executing) {
        const auto message = reinterpret_cast<const CWPSTRUCT*>(lp);
        if (message->message == RegisterWindowMessageW(MessageName)) {
            auto name = MappingName(static_cast<DWORD>(message->wParam), static_cast<DWORD>(message->lParam));
            auto mapping = OpenFileMappingW(FILE_MAP_READ | FILE_MAP_WRITE, FALSE, name.c_str());
            if (mapping) {
                auto request = static_cast<Request*>(MapViewOfFile(mapping, FILE_MAP_READ | FILE_MAP_WRITE, 0, 0, sizeof(Request)));
                if (request) {
                    if (request->magic == Magic && request->size == sizeof(Request) && request->window == message->hwnd &&
                        request->operation <= 3 && !request->complete) {
                        executing = true;
                        try { request->result = Execute(*request); } catch (...) { request->result = E_FAIL; }
                        InterlockedExchange(&request->complete, 1);
                        executing = false;
                    }
                    UnmapViewOfFile(request);
                }
                CloseHandle(mapping);
            }
        }
    }
    return CallNextHookEx(nullptr, code, wp, lp);
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) DisableThreadLibraryCalls(module);
    return TRUE;
}
#else
static void PrintProfile(const Profile& profile) {
    wchar_t clsid[40], guid[40];
    StringFromGUID2(profile.clsid, clsid, 40); StringFromGUID2(profile.profile, guid, 40);
    clsid[37] = guid[37] = L'\0'; // Canonical JSON GUID format, without braces.
    std::printf("{\"type\":%lu,\"language\":%u,\"clsid\":\"%ls\",\"profile\":\"%ls\",\"keyboard\":\"%llX\"}",
        profile.type, profile.language, clsid + 1, guid + 1, static_cast<unsigned long long>(profile.keyboard));
}
int wmain(int argc, wchar_t** argv) {
    if (argc != 5 && argc != 10) return 2;
    Request request{};
    request.magic = Magic; request.size = sizeof(request);
    request.operation = std::wstring(argv[1]) == L"query" ? 0 : std::wstring(argv[1]) == L"switch" ? 1 :
        std::wstring(argv[1]) == L"switch-fallback" ? 3 : 2; // Used only by the fallback integration check.
    request.window = reinterpret_cast<HWND>(static_cast<UINT_PTR>(_wcstoui64(argv[2], nullptr, 16)));
    request.process = wcstoul(argv[3], nullptr, 10); request.thread = wcstoul(argv[4], nullptr, 10);
    request.result = E_FAIL;
    if (request.operation == 2) {
        if (argc != 10 || std::wstring(argv[1]) != L"restore") return 2;
        request.desired.type = wcstoul(argv[5], nullptr, 10);
        request.desired.language = static_cast<WORD>(wcstoul(argv[6], nullptr, 10));
        if (FAILED(CLSIDFromString(argv[7], &request.desired.clsid)) || FAILED(CLSIDFromString(argv[8], &request.desired.profile))) return 2;
        request.desired.keyboard = static_cast<UINT_PTR>(_wcstoui64(argv[9], nullptr, 16));
    }
    DWORD process = 0;
    auto thread = GetWindowThreadProcessId(request.window, &process);
    if (!thread || !process || thread != request.thread || process != request.process) request.result = HRESULT_FROM_WIN32(ERROR_INVALID_WINDOW_HANDLE);
    else {
        wchar_t executable[32768]; GetModuleFileNameW(nullptr, executable, 32768);
        std::wstring path(executable); path.resize(path.find_last_of(L"\\/"));
#ifdef _WIN64
        auto module = LoadLibraryW((path + L"\\InputMethodHook.x64.dll").c_str());
#else
        auto module = LoadLibraryW((path + L"\\InputMethodHook.x86.dll").c_str());
#endif
        auto proc = module ? reinterpret_cast<HOOKPROC>(GetProcAddress(module, "InputMethodHook")) : nullptr;
        if (!proc && module) proc = reinterpret_cast<HOOKPROC>(GetProcAddress(module, "_InputMethodHook@12"));
        auto token = static_cast<DWORD>(GetTickCount64()) ^ GetCurrentThreadId();
        auto name = MappingName(GetCurrentProcessId(), token);
        auto mapping = CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(Request), name.c_str());
        auto shared = mapping ? static_cast<Request*>(MapViewOfFile(mapping, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(Request))) : nullptr;
        if (proc && shared) {
            *shared = request;
            auto hook = SetWindowsHookExW(WH_CALLWNDPROC, proc, module, thread);
            if (hook) {
                DWORD_PTR ignored = 0;
                SendMessageTimeoutW(request.window, RegisterWindowMessageW(MessageName), GetCurrentProcessId(), token,
                    SMTO_ABORTIFHUNG | SMTO_ERRORONEXIT, 3000, &ignored);
                if (shared->complete) request = *shared;
                else request.result = HRESULT_FROM_WIN32(ERROR_TIMEOUT);
                UnhookWindowsHookEx(hook);
            } else request.result = HRESULT_FROM_WIN32(GetLastError());
        } else request.result = HRESULT_FROM_WIN32(GetLastError());
        if (shared) UnmapViewOfFile(shared);
        if (mapping) CloseHandle(mapping);
        if (module) FreeLibrary(module);
    }
    std::printf("{\"ok\":%s,\"error\":\"0x%08lX\",\"before\":", request.result == S_OK ? "true" : "false", static_cast<unsigned long>(request.result));
    PrintProfile(request.before); std::printf(",\"after\":"); PrintProfile(request.after); std::puts("}");
    return request.result == S_OK ? 0 : 1;
}
#endif
