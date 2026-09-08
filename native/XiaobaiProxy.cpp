// Version-independent extension: load the installed vendor TIP and use only public
// TSF interfaces. No vendor structures, IPC messages, DLL bytes or dictionaries change.
#define GAMEPADT9_HOSTED
#include "Bridge.cpp"
#include <mutex>

static const CLSID XiaobaiId = {0xa3f4cded,0xb1e9,0x41ee,{0x9c,0xa6,0x7b,0x4d,0x0d,0xe6,0xcb,0x0a}};
static HMODULE originalModule = nullptr;
static std::mutex loadMutex;
using GetFactory = HRESULT (STDAPICALLTYPE*)(REFCLSID, REFIID, void**);

static HRESULT OriginalFactory(IClassFactory** out) {
    std::lock_guard<std::mutex> guard(loadMutex);
    if (!originalModule) {
        wchar_t modulePath[32768], originalPath[32768];
        if (!GetModuleFileNameW(instance, modulePath, ARRAYSIZE(modulePath))) return E_FAIL;
        std::wstring ini(modulePath); ini.resize(ini.find_last_of(L"\\/") + 1); ini += L"original.ini";
        if (!GetPrivateProfileStringW(L"Original", L"Path", L"", originalPath, ARRAYSIZE(originalPath), ini.c_str())) return E_FAIL;
        if (!_wcsicmp(modulePath, originalPath)) return E_INVALIDARG;
        originalModule = LoadLibraryExW(originalPath, nullptr, LOAD_WITH_ALTERED_SEARCH_PATH);
        if (!originalModule) return HRESULT_FROM_WIN32(GetLastError());
    }
    auto get = reinterpret_cast<GetFactory>(GetProcAddress(originalModule, "DllGetClassObject"));
    return get ? get(XiaobaiId, IID_IClassFactory, reinterpret_cast<void**>(out)) : E_NOINTERFACE;
}

class NumericSink final : public ITfKeyEventSink {
    std::atomic<ULONG> refs{1};
    ComPtr<ITfKeyEventSink> original;
    static bool Bypass(WPARAM key, BOOL* eaten) {
        if (eaten && key >= VK_NUMPAD0 && key <= VK_NUMPAD9 && GetTickCount64() <= numericUntil &&
            static_cast<ULONG_PTR>(GetMessageExtraInfo()) == NumericMarker) { *eaten = FALSE; return true; }
        return false;
    }
public:
    explicit NumericSink(ITfKeyEventSink* value) : original(value) { ++objects; }
    ~NumericSink() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (iid != IID_IUnknown && iid != IID_ITfKeyEventSink) return E_NOINTERFACE;
        *out = static_cast<ITfKeyEventSink*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE OnSetFocus(BOOL value) override { return original->OnSetFocus(value); }
    HRESULT STDMETHODCALLTYPE OnTestKeyDown(ITfContext* c, WPARAM w, LPARAM l, BOOL* e) override { return Bypass(w,e) ? S_OK : original->OnTestKeyDown(c,w,l,e); }
    HRESULT STDMETHODCALLTYPE OnTestKeyUp(ITfContext* c, WPARAM w, LPARAM l, BOOL* e) override { return Bypass(w,e) ? S_OK : original->OnTestKeyUp(c,w,l,e); }
    HRESULT STDMETHODCALLTYPE OnKeyDown(ITfContext* c, WPARAM w, LPARAM l, BOOL* e) override { return Bypass(w,e) ? S_OK : original->OnKeyDown(c,w,l,e); }
    HRESULT STDMETHODCALLTYPE OnKeyUp(ITfContext* c, WPARAM w, LPARAM l, BOOL* e) override { return Bypass(w,e) ? S_OK : original->OnKeyUp(c,w,l,e); }
    HRESULT STDMETHODCALLTYPE OnPreservedKey(ITfContext* c, REFGUID g, BOOL* e) override { return original->OnPreservedKey(c,g,e); }
};

class ExtendedTip final : public ITfTextInputProcessorEx {
    std::atomic<ULONG> refs{1};
    ComPtr<ITfTextInputProcessor> original;
    ComPtr<Service> bridge;
    bool activated = false;
    static bool Ready(void* data) {
        auto manager = static_cast<ITfThreadMgr*>(data);
        ComPtr<ITfDocumentMgr> doc; ComPtr<ITfContext> context;
        if (FAILED(manager->GetFocus(&doc)) || !doc || FAILED(doc->GetTop(&context)) || !context) return false;
        ComPtr<ITfContextComposition> compositions; ComPtr<IEnumITfCompositionView> items;
        if (FAILED(context.As(&compositions)) || FAILED(compositions->EnumCompositions(&items))) return false;
        ComPtr<ITfCompositionView> item; ULONG count = 0;
        if (FAILED(items->Next(1, &item, &count)) || count) return false;
        ComPtr<ITfCompartmentMgr> compartments; ComPtr<ITfCompartment> disabled;
        if (SUCCEEDED(context.As(&compartments)) && SUCCEEDED(compartments->GetCompartment(GUID_COMPARTMENT_KEYBOARD_DISABLED, &disabled))) {
            VARIANT value; VariantInit(&value);
            auto hr = disabled->GetValue(&value); bool blocked = SUCCEEDED(hr) && value.vt == VT_I4 && value.lVal;
            VariantClear(&value); if (blocked) return false;
        }
        return true;
    }
public:
    explicit ExtendedTip(ITfTextInputProcessor* value) : original(value) { ++objects; }
    ~ExtendedTip() { Deactivate(); --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (iid == IID_IUnknown || iid == IID_ITfTextInputProcessor || iid == IID_ITfTextInputProcessorEx) {
            *out = static_cast<ITfTextInputProcessorEx*>(this); AddRef(); return S_OK;
        }
        return original->QueryInterface(iid, out);
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE Activate(ITfThreadMgr* manager, TfClientId id) override { return ActivateEx(manager,id,0); }
    HRESULT STDMETHODCALLTYPE ActivateEx(ITfThreadMgr* manager, TfClientId id, DWORD flags) override {
        if (activated || !manager) return E_UNEXPECTED;
        ComPtr<ITfTextInputProcessorEx> extended;
        auto hr = original.As(&extended);
        hr = SUCCEEDED(hr) ? extended->ActivateEx(manager,id,flags) : original->Activate(manager,id);
        if (FAILED(hr)) return hr;
        activated = true;
        // Replace only this TIP's key sink; real keyboard events still go to the vendor.
        ComPtr<ITfKeyEventSink> sink; ComPtr<ITfKeystrokeMgr> keys;
        hr = original.As(&sink);
        if (SUCCEEDED(hr)) hr = manager->QueryInterface(IID_PPV_ARGS(&keys));
        if (SUCCEEDED(hr)) hr = keys->UnadviseKeyEventSink(id);
        if (SUCCEEDED(hr)) {
            auto filter = new NumericSink(sink.Get());
            hr = keys->AdviseKeyEventSink(id, filter, TRUE); filter->Release();
            if (FAILED(hr)) keys->AdviseKeyEventSink(id, sink.Get(), TRUE);
        }
        if (SUCCEEDED(hr)) {
            bridge.Attach(new Service);
            bridge->SetHost(original.Get(), Ready, manager);
            hr = bridge->ActivateEx(manager,id,flags);
        }
        if (FAILED(hr)) Deactivate();
        return hr;
    }
    HRESULT STDMETHODCALLTYPE Deactivate() override {
        if (bridge) { bridge->Deactivate(); bridge.Reset(); }
        if (!activated) return S_OK;
        activated = false; return original->Deactivate();
    }
};

class ExtensionFactory final : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    ExtensionFactory() { ++objects; } ~ExtensionFactory() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *out = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (outer) return CLASS_E_NOAGGREGATION;
        ComPtr<IClassFactory> factory; auto hr = OriginalFactory(&factory);
        ComPtr<ITfTextInputProcessor> tip;
        if (SUCCEEDED(hr)) hr = factory->CreateInstance(nullptr, IID_PPV_ARGS(&tip));
        if (FAILED(hr)) return hr;
        auto wrapped = new ExtendedTip(tip.Get()); hr = wrapped->QueryInterface(iid,out); wrapped->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++objects; else --objects; return S_OK; }
};
STDAPI DllGetClassObject(REFCLSID id, REFIID iid, void** out) {
    if (id != XiaobaiId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new ExtensionFactory; auto hr = factory->QueryInterface(iid,out); factory->Release(); return hr;
}
// Vendor interfaces can be held independently by TSF. Keep both modules resident once
// loaded; do not unload vendor code while one of its delegated interfaces is in use.
STDAPI DllCanUnloadNow() { return !originalModule && objects == 0 ? S_OK : S_FALSE; }
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { instance = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
