#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <dwmapi.h>
#include <DispatcherQueue.h>
#include <d2d1effects.h>
#include <windows.graphics.effects.interop.h>
#include <windows.ui.composition.interop.h>
#include <winrt/Windows.Foundation.h>
#include <winrt/Windows.Foundation.Collections.h>
#include <winrt/Windows.Graphics.Effects.h>
#include <winrt/Windows.System.h>
#include <winrt/Windows.UI.Composition.h>
#include <winrt/Windows.UI.Composition.Desktop.h>
#include <memory>

using namespace winrt;
using namespace Windows::Foundation;
using namespace Windows::Graphics::Effects;
using namespace Windows::UI::Composition;
using namespace Windows::UI::Composition::Desktop;
using EffectInterop = ABI::Windows::Graphics::Effects::IGraphicsEffectD2D1Interop;
using Mapping = ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING;

// Describe a single Direct2D Gaussian blur without shipping Win2D or Windows App SDK.
struct BlurEffect : implements<BlurEffect, IGraphicsEffect, IGraphicsEffectSource, EffectInterop>
{
    hstring name{ L"Blur" };
    IGraphicsEffectSource source{ nullptr };
    float amount = 0;
    hstring Name() const { return name; }
    void Name(hstring const& value) { name = value; }
    HRESULT __stdcall GetEffectId(GUID* id) noexcept override { if (!id) return E_POINTER; *id = CLSID_D2D1GaussianBlur; return S_OK; }
    HRESULT __stdcall GetNamedPropertyMapping(LPCWSTR property, UINT* index, Mapping* mapping) noexcept override
    {
        if (!property || !index || !mapping) return E_POINTER;
        if (wcscmp(property, L"BlurAmount") != 0) return E_INVALIDARG;
        *index = 0; *mapping = ABI::Windows::Graphics::Effects::GRAPHICS_EFFECT_PROPERTY_MAPPING_DIRECT; return S_OK;
    }
    HRESULT __stdcall GetPropertyCount(UINT* count) noexcept override { if (!count) return E_POINTER; *count = 3; return S_OK; }
    HRESULT __stdcall GetProperty(UINT index, ABI::Windows::Foundation::IPropertyValue** value) noexcept override
    {
        if (!value) return E_POINTER; *value = nullptr;
        try
        {
            Windows::Foundation::IInspectable boxed{ nullptr };
            if (index == 0) boxed = PropertyValue::CreateSingle(amount);
            else if (index == 1) boxed = PropertyValue::CreateUInt32(D2D1_GAUSSIANBLUR_OPTIMIZATION_BALANCED);
            else if (index == 2) boxed = PropertyValue::CreateUInt32(D2D1_BORDER_MODE_HARD);
            else return E_INVALIDARG;
            return boxed.as<::IInspectable>()->QueryInterface(__uuidof(**value), reinterpret_cast<void**>(value));
        }
        catch (...) { return to_hresult(); }
    }
    HRESULT __stdcall GetSource(UINT index, ABI::Windows::Graphics::Effects::IGraphicsEffectSource** value) noexcept override
    {
        if (!value) return E_POINTER; *value = nullptr;
        if (index != 0) return E_INVALIDARG;
        return source.as<::IInspectable>()->QueryInterface(__uuidof(**value), reinterpret_cast<void**>(value));
    }
    HRESULT __stdcall GetSourceCount(UINT* count) noexcept override { if (!count) return E_POINTER; *count = 1; return S_OK; }
};

struct Backdrop
{
    Compositor compositor{ nullptr };
    DesktopWindowTarget target{ nullptr };
    CompositionEffectBrush brush{ nullptr };
    SpriteVisual visual{ nullptr };
    Backdrop(HWND window, float amount)
    {
        BOOL enabled = TRUE;
        DwmSetWindowAttribute(window, DWMWA_TRANSITIONS_FORCEDISABLED, &enabled, sizeof(enabled));
        // Each UI thread owns one dispatcher queue; all surfaces share its lifetime.
        static thread_local Windows::System::DispatcherQueueController queue{ nullptr };
        if (!Windows::System::DispatcherQueue::GetForCurrentThread())
        {
            DispatcherQueueOptions options{ sizeof(options), DQTYPE_THREAD_CURRENT, DQTAT_COM_NONE };
            check_hresult(CreateDispatcherQueueController(options, reinterpret_cast<ABI::Windows::System::IDispatcherQueueController**>(put_abi(queue))));
        }
        compositor = Compositor();
        auto interop = compositor.as<ABI::Windows::UI::Composition::Desktop::ICompositorDesktopInterop>();
        check_hresult(interop->CreateDesktopWindowTarget(window, false, reinterpret_cast<ABI::Windows::UI::Composition::Desktop::IDesktopWindowTarget**>(put_abi(target))));
        auto effect = make_self<BlurEffect>();
        effect->source = CompositionEffectSourceParameter(L"Backdrop"); effect->amount = amount;
        brush = compositor.CreateEffectFactory(effect.as<IGraphicsEffect>(), { L"Blur.BlurAmount" }).CreateBrush();
        brush.SetSourceParameter(L"Backdrop", compositor.CreateBackdropBrush());
        visual = compositor.CreateSpriteVisual(); visual.RelativeSizeAdjustment({ 1, 1 }); visual.Brush(brush);
        target.Root(visual);
    }
    ~Backdrop() noexcept { try { target.Root(nullptr); target.Close(); compositor.Close(); } catch (...) { } }
};

extern "C" __declspec(dllexport) HRESULT __cdecl BackdropCreate(HWND window, float amount, void** instance) noexcept
{
    if (!instance) return E_POINTER; *instance = nullptr;
    try { *instance = new Backdrop(window, amount); return S_OK; }
    catch (...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) HRESULT __cdecl BackdropSetBlur(void* instance, float amount) noexcept
{
    if (!instance || amount < 0 || amount > 250) return E_INVALIDARG;
    try { static_cast<Backdrop*>(instance)->brush.Properties().InsertScalar(L"Blur.BlurAmount", amount); return S_OK; }
    catch (...) { return to_hresult(); }
}
extern "C" __declspec(dllexport) void __cdecl BackdropDestroy(void* instance) noexcept
{
    try { delete static_cast<Backdrop*>(instance); } catch (...) { }
}
