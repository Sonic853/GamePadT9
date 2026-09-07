// Minimal in-process TSF text service. No keyboard hooks or synthetic input.
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <msctf.h>
#include <wrl/client.h>
#include <string>
#include <atomic>
#include <cstdint>
#include <cstring>
using Microsoft::WRL::ComPtr;

static const CLSID ServiceId = {0x595b67e9,0x48a3,0x4c82,{0xb7,0xb1,0x64,0xe4,0xa3,0x5c,0x9d,0x92}};
static const GUID ProfileId = {0x79c457d1,0x690a,0x4f83,{0xa3,0xde,0xc9,0x5c,0x98,0xe0,0x1d,0x4d}};
static constexpr wchar_t ClassName[] = L"GamePadT9.TextService.v1";
static constexpr wchar_t Description[] = L"GamePad T9 验证";
static constexpr UINT ProbeMessage = WM_APP + 91, ReceiptMessage = WM_APP + 92, ErrorMessage = WM_APP + 93;
static constexpr ULONG_PTR WireMagic = 0x39545047;
static HINSTANCE instance;
static std::atomic<long> objects{0};

class Service;
class EditSession final : public ITfEditSession {
    std::atomic<ULONG> refs{1};
    Service* owner;
    ComPtr<ITfContext> context;
    HWND foreground;
    uint32_t epoch, request;
    std::wstring text;
public:
    EditSession(Service*, ITfContext*, HWND, uint32_t, uint32_t, std::wstring);
    ~EditSession();
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid != IID_IUnknown && iid != IID_ITfEditSession) return E_NOINTERFACE;
        *out = static_cast<ITfEditSession*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE DoEditSession(TfEditCookie cookie) override;
};

class Service final : public ITfTextInputProcessorEx, public ITfThreadMgrEventSink, public ITfThreadFocusSink {
    std::atomic<ULONG> refs{1};
    ComPtr<ITfThreadMgr> manager;
    ComPtr<ITfContext> lastContext;
    TfClientId clientId = TF_CLIENTID_NULL;
    DWORD mgrCookie = TF_INVALID_COOKIE, focusCookie = TF_INVALID_COOKIE;
    HWND endpoint = nullptr;
    uint32_t epoch = 1, receiptId = 0;
    int receiptState = 0;
    HRESULT receiptError = S_OK;
    bool active = false;
    void Changed() { if (++epoch > 0x7fffffff) epoch = 1; lastContext.Reset(); }
    ComPtr<ITfContext> Focused() {
        ComPtr<ITfDocumentMgr> doc; ComPtr<ITfContext> ctx;
        if (active && manager && SUCCEEDED(manager->GetFocus(&doc)) && doc) doc->GetTop(&ctx);
        return ctx;
    }
    static bool Same(ITfContext* a, ITfContext* b) {
        if (!a || !b) return a == b;
        ComPtr<IUnknown> x, y;
        a->QueryInterface(IID_PPV_ARGS(&x)); b->QueryInterface(IID_PPV_ARGS(&y));
        return x.Get() == y.Get();
    }
    bool IsForeground() {
        DWORD pid = 0; GetWindowThreadProcessId(GetForegroundWindow(), &pid);
        BOOL focused = FALSE;
        return pid == GetCurrentProcessId() && manager &&
            SUCCEEDED(manager->IsThreadFocus(&focused)) && focused;
    }
    uint32_t Probe() {
        if (!IsForeground()) return 0;
        auto ctx = Focused();
        if (!ctx) return 0;
        TF_STATUS status{};
        if (FAILED(ctx->GetStatus(&status)) || (status.dwDynamicFlags & TF_SD_READONLY)) return 0;
        if (!Same(lastContext.Get(), ctx.Get())) { Changed(); lastContext = ctx; }
        return epoch;
    }
    LRESULT Accept(HWND foreground, const COPYDATASTRUCT* packet) {
        if (!packet || packet->dwData != WireMagic || !packet->lpData ||
            packet->cbData < 16 || packet->cbData > 16 + 2048) return 0;
        uint32_t header[4]; std::memcpy(header, packet->lpData, 16);
        const auto [version, expectedEpoch, id, chars] = header;
        if (version != 1 || id == 0 || chars == 0 || chars > 1024 ||
            packet->cbData != 16 + chars * sizeof(wchar_t) || receiptState == 1) return 0;
        if (foreground != GetForegroundWindow() || Probe() != expectedEpoch) return 0;
        auto ctx = Focused(); if (!ctx) return 0;
        const auto data = reinterpret_cast<const wchar_t*>(static_cast<const BYTE*>(packet->lpData) + 16);
        std::wstring text(data, chars);
        if (text.find(L'\0') != std::wstring::npos) return 0;
        if (id == receiptId) return receiptState == 2 ? 1 : 0;
        receiptId = id; receiptState = 1; receiptError = S_OK;
        auto edit = new EditSession(this, ctx.Get(), foreground, expectedEpoch, id, std::move(text));
        HRESULT sessionResult = E_FAIL;
        // IPC is not a keystroke callback: allow TSF to schedule the document lock.
        auto hr = ctx->RequestEditSession(clientId, edit, TF_ES_ASYNCDONTCARE | TF_ES_READWRITE, &sessionResult);
        edit->Release();
        if (FAILED(hr) || FAILED(sessionResult)) { Complete(id, FAILED(hr) ? hr : sessionResult); return 0; }
        return 1;
    }
    static LRESULT CALLBACK WindowProc(HWND window, UINT msg, WPARAM wp, LPARAM lp) {
        auto self = reinterpret_cast<Service*>(GetWindowLongPtrW(window, GWLP_USERDATA));
        if (msg == WM_NCCREATE) {
            self = static_cast<Service*>(reinterpret_cast<CREATESTRUCTW*>(lp)->lpCreateParams);
            SetWindowLongPtrW(window, GWLP_USERDATA, reinterpret_cast<LONG_PTR>(self));
        }
        if (self) {
            if (msg == ProbeMessage) return self->Probe();
            if (msg == ReceiptMessage) return wp == self->receiptId ? self->receiptState : 0;
            if (msg == ErrorMessage) return self->receiptError;
            if (msg == WM_COPYDATA) {
                try { return self->Accept(reinterpret_cast<HWND>(wp), reinterpret_cast<COPYDATASTRUCT*>(lp)); }
                catch (...) { return 0; }
            }
        }
        return DefWindowProcW(window, msg, wp, lp);
    }
public:
    Service() { ++objects; }
    ~Service() { Deactivate(); --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER;
        *out = nullptr;
        if (iid == IID_IUnknown || iid == IID_ITfTextInputProcessor || iid == IID_ITfTextInputProcessorEx)
            *out = static_cast<ITfTextInputProcessorEx*>(this);
        else if (iid == IID_ITfThreadMgrEventSink) *out = static_cast<ITfThreadMgrEventSink*>(this);
        else if (iid == IID_ITfThreadFocusSink) *out = static_cast<ITfThreadFocusSink*>(this);
        else return E_NOINTERFACE;
        AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE Activate(ITfThreadMgr* mgr, TfClientId id) override { return ActivateEx(mgr, id, 0); }
    HRESULT STDMETHODCALLTYPE ActivateEx(ITfThreadMgr* mgr, TfClientId id, DWORD) override {
        if (!mgr || active) return E_UNEXPECTED;
        manager = mgr; clientId = id; active = true;
        ComPtr<ITfSource> source;
        HRESULT hr = manager.As(&source);
        if (SUCCEEDED(hr)) hr = source->AdviseSink(IID_ITfThreadMgrEventSink, static_cast<ITfThreadMgrEventSink*>(this), &mgrCookie);
        if (SUCCEEDED(hr)) hr = source->AdviseSink(IID_ITfThreadFocusSink, static_cast<ITfThreadFocusSink*>(this), &focusCookie);
        if (FAILED(hr)) { Deactivate(); return hr; }
        WNDCLASSEXW wc{sizeof(wc)};
        wc.lpfnWndProc = WindowProc; wc.hInstance = instance; wc.lpszClassName = ClassName;
        if (!RegisterClassExW(&wc) && GetLastError() != ERROR_CLASS_ALREADY_EXISTS) { Deactivate(); return E_FAIL; }
        endpoint = CreateWindowExW(0, ClassName, L"", 0, 0, 0, 0, 0, HWND_MESSAGE, nullptr, instance, this);
        if (!endpoint) { Deactivate(); return E_FAIL; }
        return S_OK;
    }
    HRESULT STDMETHODCALLTYPE Deactivate() override {
        active = false; Changed();
        if (endpoint) { DestroyWindow(endpoint); endpoint = nullptr; }
        // DLL-owned window classes survive unload unless explicitly unregistered.
        // If another service instance still has a window, Windows keeps the class.
        UnregisterClassW(ClassName, instance);
        if (manager) {
            ComPtr<ITfSource> source;
            if (SUCCEEDED(manager.As(&source))) {
                if (mgrCookie != TF_INVALID_COOKIE) source->UnadviseSink(mgrCookie);
                if (focusCookie != TF_INVALID_COOKIE) source->UnadviseSink(focusCookie);
            }
        }
        mgrCookie = focusCookie = TF_INVALID_COOKIE;
        manager.Reset(); clientId = TF_CLIENTID_NULL;
        return S_OK;
    }
    bool Validate(ITfContext* ctx, HWND foreground, uint32_t expected) {
        return active && foreground == GetForegroundWindow() && IsForeground() && expected == epoch && Same(ctx, Focused().Get());
    }
    void Complete(uint32_t id, HRESULT hr) {
        if (id == receiptId) { receiptState = SUCCEEDED(hr) ? 2 : 3; receiptError = hr; }
    }
    HRESULT STDMETHODCALLTYPE OnInitDocumentMgr(ITfDocumentMgr*) override { return S_OK; }
    HRESULT STDMETHODCALLTYPE OnUninitDocumentMgr(ITfDocumentMgr*) override { Changed(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnSetFocus(ITfDocumentMgr*, ITfDocumentMgr*) override { Changed(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPushContext(ITfContext*) override { Changed(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnPopContext(ITfContext*) override { Changed(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnSetThreadFocus() override { Changed(); return S_OK; }
    HRESULT STDMETHODCALLTYPE OnKillThreadFocus() override { Changed(); return S_OK; }
};

EditSession::EditSession(Service* svc, ITfContext* ctx, HWND fg, uint32_t token, uint32_t id, std::wstring value)
    : owner(svc), context(ctx), foreground(fg), epoch(token), request(id), text(std::move(value)) { owner->AddRef(); }
EditSession::~EditSession() { owner->Release(); }
HRESULT EditSession::DoEditSession(TfEditCookie cookie) {
    HRESULT hr = E_ABORT;
    if (owner->Validate(context.Get(), foreground, epoch)) {
        ComPtr<ITfInsertAtSelection> insertion; ComPtr<ITfRange> range;
        hr = context.As(&insertion);
        if (SUCCEEDED(hr)) hr = insertion->InsertTextAtSelection(cookie, 0, text.data(), static_cast<LONG>(text.size()), &range);
        // A successful insertion is never retried, even if moving the caret fails.
        if (SUCCEEDED(hr) && range && SUCCEEDED(range->Collapse(cookie, TF_ANCHOR_END))) {
            TF_SELECTION selection{range.Get(), {TF_AE_NONE, FALSE}};
            context->SetSelection(cookie, 1, &selection);
        }
    }
    owner->Complete(request, hr);
    return hr;
}

class Factory final : public IClassFactory {
    std::atomic<ULONG> refs{1};
public:
    Factory() { ++objects; } ~Factory() { --objects; }
    HRESULT STDMETHODCALLTYPE QueryInterface(REFIID iid, void** out) override {
        if (!out) return E_POINTER; *out = nullptr;
        if (iid != IID_IUnknown && iid != IID_IClassFactory) return E_NOINTERFACE;
        *out = static_cast<IClassFactory*>(this); AddRef(); return S_OK;
    }
    ULONG STDMETHODCALLTYPE AddRef() override { return ++refs; }
    ULONG STDMETHODCALLTYPE Release() override { auto n = --refs; if (!n) delete this; return n; }
    HRESULT STDMETHODCALLTYPE CreateInstance(IUnknown* outer, REFIID iid, void** out) override {
        if (outer) return CLASS_E_NOAGGREGATION;
        auto svc = new Service; auto hr = svc->QueryInterface(iid, out); svc->Release(); return hr;
    }
    HRESULT STDMETHODCALLTYPE LockServer(BOOL lock) override { if (lock) ++objects; else --objects; return S_OK; }
};

STDAPI DllGetClassObject(REFCLSID id, REFIID iid, void** out) {
    if (id != ServiceId) return CLASS_E_CLASSNOTAVAILABLE;
    auto factory = new Factory; auto hr = factory->QueryInterface(iid, out); factory->Release(); return hr;
}
STDAPI DllCanUnloadNow() { return objects == 0 ? S_OK : S_FALSE; }

static std::wstring RegistryPath() {
    wchar_t guid[40]; StringFromGUID2(ServiceId, guid, 40);
    return std::wstring(L"Software\\Classes\\CLSID\\") + guid;
}
static HRESULT RegisterCom() {
    wchar_t path[32768]; auto n = GetModuleFileNameW(instance, path, 32768);
    if (!n || n >= 32768) return E_FAIL;
    HKEY key = nullptr;
    auto subkey = RegistryPath() + L"\\InprocServer32";
    auto status = RegCreateKeyExW(HKEY_CURRENT_USER, subkey.c_str(), 0, nullptr, 0, KEY_WRITE, nullptr, &key, nullptr);
    if (status != ERROR_SUCCESS) return HRESULT_FROM_WIN32(status);
    status = RegSetValueExW(key, nullptr, 0, REG_SZ, reinterpret_cast<const BYTE*>(path), (n + 1) * sizeof(wchar_t));
    if (status == ERROR_SUCCESS) status = RegSetValueExW(key, L"ThreadingModel", 0, REG_SZ, reinterpret_cast<const BYTE*>(L"Apartment"), sizeof(L"Apartment"));
    RegCloseKey(key); return HRESULT_FROM_WIN32(status);
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllUnregisterServer() {
    auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ComPtr<ITfInputProcessorProfiles> profiles; ComPtr<ITfCategoryMgr> categories;
    auto hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr)) hr = profiles->Unregister(ServiceId);
    if (SUCCEEDED(CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&categories)))) {
        categories->UnregisterCategory(ServiceId, GUID_TFCAT_TIP_KEYBOARD, ServiceId);
        categories->UnregisterCategory(ServiceId, GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT, ServiceId);
    }
    RegDeleteTreeW(HKEY_CURRENT_USER, RegistryPath().c_str());
    profiles.Reset(); categories.Reset(); if (SUCCEEDED(init)) CoUninitialize(); return hr;
}
extern "C" __declspec(dllexport) HRESULT __stdcall DllRegisterServer() {
    auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    auto hr = RegisterCom();
    ComPtr<ITfInputProcessorProfiles> profiles; ComPtr<ITfCategoryMgr> categories;
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr)) hr = profiles->Register(ServiceId);
    if (SUCCEEDED(hr)) hr = profiles->AddLanguageProfile(ServiceId, 0x0804, ProfileId, Description, ARRAYSIZE(Description) - 1, nullptr, 0, 0);
    if (SUCCEEDED(hr)) hr = profiles->EnableLanguageProfile(ServiceId, 0x0804, ProfileId, TRUE);
    if (SUCCEEDED(hr)) hr = CoCreateInstance(CLSID_TF_CategoryMgr, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&categories));
    if (SUCCEEDED(hr)) hr = categories->RegisterCategory(ServiceId, GUID_TFCAT_TIP_KEYBOARD, ServiceId);
    if (SUCCEEDED(hr)) hr = categories->RegisterCategory(ServiceId, GUID_TFCAT_TIPCAP_IMMERSIVESUPPORT, ServiceId);
    profiles.Reset(); categories.Reset();
    if (FAILED(hr)) DllUnregisterServer();
    if (SUCCEEDED(init)) CoUninitialize(); return hr;
}
extern "C" __declspec(dllexport) HRESULT __stdcall ActivateForSession() {
    auto init = CoInitializeEx(nullptr, COINIT_APARTMENTTHREADED);
    ComPtr<ITfInputProcessorProfileMgr> profiles;
    auto hr = CoCreateInstance(CLSID_TF_InputProcessorProfiles, nullptr, CLSCTX_INPROC_SERVER, IID_PPV_ARGS(&profiles));
    if (SUCCEEDED(hr)) hr = profiles->ActivateProfile(TF_PROFILETYPE_INPUTPROCESSOR, 0x0804, ServiceId, ProfileId, nullptr, TF_IPPMF_FORSESSION);
    profiles.Reset(); if (SUCCEEDED(init)) CoUninitialize(); return hr;
}
BOOL WINAPI DllMain(HINSTANCE module, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) { instance = module; DisableThreadLibraryCalls(module); }
    return TRUE;
}
