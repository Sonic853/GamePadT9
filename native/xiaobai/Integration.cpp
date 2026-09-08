// Compiled into the original xiaobai WeaselTSF DLL, not a second TIP.
#include "WeaselTSF.h"
#define GAMEPADT9_HOSTED
#include "../Bridge.cpp"

void WeaselTSF::_InitGamePadBridge() {
    if (_gamePadBridge) return;
    instance = g_hInst;
    auto service = new Service;
    service->SetHost(static_cast<ITfTextInputProcessorEx*>(this), [](void* data) {
        auto host = static_cast<WeaselTSF*>(data);
        // Never insert inside a physical-keyboard composition owned by xiaobai.
        return host->_gamePadServerCompatible && !host->_IsComposing() && !host->_IsKeyboardDisabled();
    }, this);
    if (SUCCEEDED(service->ActivateEx(_pThreadMgr, _tfClientId, _activateFlags)))
        _gamePadBridge = service;
    else service->Release();
}

void WeaselTSF::_UninitGamePadBridge() {
    auto service = static_cast<Service*>(_gamePadBridge);
    _gamePadBridge = nullptr;
    if (service) { service->Deactivate(); service->Release(); }
}

bool WeaselTSF::_IsGamePadNumericKey(WPARAM key) {
    return key >= VK_NUMPAD0 && key <= VK_NUMPAD9 &&
        GetTickCount64() <= numericUntil &&
        static_cast<ULONG_PTR>(GetMessageExtraInfo()) == NumericMarker;
}
