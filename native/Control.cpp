#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <cstdio>
#include <string>
int wmain(int argc, wchar_t** argv) {
    if (argc != 2) { std::fwprintf(stderr, L"Usage: BridgeControl register|unregister|activate\n"); return 2; }
    wchar_t path[32768]; GetModuleFileNameW(nullptr, path, 32768);
    std::wstring folder(path); folder.resize(folder.find_last_of(L"\\/"));
    auto module = LoadLibraryW((folder + L"\\GamePadT9.TextService.dll").c_str());
    if (!module) { std::fprintf(stderr, "LoadLibrary error: %lu\n", GetLastError()); return 1; }
    const std::wstring action = argv[1];
    const char* name = action == L"register" ? "DllRegisterServer" : action == L"unregister" ? "DllUnregisterServer" : action == L"activate" ? "ActivateForSession" : nullptr;
    auto proc = name ? reinterpret_cast<HRESULT(__stdcall*)()>(GetProcAddress(module, name)) : nullptr;
    auto hr = proc ? proc() : E_INVALIDARG;
    std::printf("%ls: HRESULT 0x%08lX\n", action.c_str(), static_cast<unsigned long>(hr));
    FreeLibrary(module); return SUCCEEDED(hr) ? 0 : 1;
}
