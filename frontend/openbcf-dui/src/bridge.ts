// Thin wrapper around two different native bridges, both driven by OpenBcf.Dui's IBinding/
// BrowserBridge shape (see src/OpenBcf.Dui/Bridge/BrowserBridge.cs):
//
//  - WebView2 (Revit/Tekla/Rhino): each C# IBinding is exposed as
//    `chrome.webview.hostObjects.sync.<bindingName>`, with exactly one method, RunMethod, that we
//    call to kick off work; the (possibly async) result comes back later through
//    window.__openbcfDuiReceiveResult, keyed by requestId, and is resolved against the matching
//    pending Promise below.
//  - ArchiCAD (DG::Browser, via ACAPI's RegisterAsynchJSObject - see
//    src/OpenBcf.ArchiCad29.NativeAddOn/Src/BcfPalette.cpp): each binding is exposed as
//    `window.<bindingName>`, with one JS function PER METHOD (not a single shared RunMethod - a
//    real, confirmed-live ACAPI limitation means a second call into the same registered function
//    can't get through while an earlier call to it is still pending, e.g. Connect blocking on a
//    project pick while ResolveProjectPick tries to answer it). ACAPI's own bridge wraps each call
//    in a real native Promise<string>, resolving directly to a JSON envelope
//    ({"isError":false,"result":...} or {"isError":true,"message":...}), so no manual
//    requestId/pending-map bookkeeping is needed on this path at all.
//
// Both transports deliver proactive server-pushed events (BrowserBridge.Send) identically, via
// window.__openbcfDuiReceiveEvent - that half needs no branching below.

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (reason: unknown) => void
}

interface AcapiRunMethodResult {
  isError: boolean
  result?: unknown
  message?: string
}

declare global {
  interface Window {
    chrome?: {
      webview?: {
        hostObjects: {
          sync: Record<string, { RunMethod(methodName: string, requestId: string, argsJson: string): void }>
        }
      }
    }
    __openbcfDuiReceiveResult?: (requestId: string, isError: boolean, payloadJson: string) => void
    __openbcfDuiReceiveEvent?: (bindingName: string, eventName: string, payloadJson: string) => void
  }
}

// window.<bindingName> (pingBinding, bcfSessionBinding, ...) - each an ACAPI-registered JS::Object
// with one function per binding method, present only when running inside ArchiCAD's DG::Browser.
// Looked up dynamically by name below rather than added to the global Window interface, to avoid a
// blanket string index signature on every DOM property.
type AcapiBindingObject = Record<string, ((argsJson: string) => Promise<string>) | undefined>

const pending = new Map<string, PendingRequest>()
const eventListeners = new Map<string, Set<(payload: unknown) => void>>()
let requestCounter = 0

window.__openbcfDuiReceiveResult = (requestId, isError, payloadJson) => {
  const request = pending.get(requestId)
  if (!request) {
    return
  }
  pending.delete(requestId)

  const payload = JSON.parse(payloadJson)
  if (isError) {
    request.reject(new Error(payload?.message ?? 'Unknown binding error'))
  } else {
    request.resolve(payload)
  }
}

window.__openbcfDuiReceiveEvent = (bindingName, eventName, payloadJson) => {
  const listeners = eventListeners.get(`${bindingName}:${eventName}`)
  if (!listeners) {
    return
  }
  const payload = JSON.parse(payloadJson)
  listeners.forEach((listener) => listener(payload))
}

/** Calls a method on a C# IBinding and resolves with its (JSON-decoded) return value. */
export function callBinding<T = unknown>(bindingName: string, methodName: string, ...args: unknown[]): Promise<T> {
  const acapiMethod = (window as unknown as Record<string, AcapiBindingObject | undefined>)[bindingName]?.[methodName]
  if (acapiMethod) {
    return acapiMethod(JSON.stringify(args)).then((resultJson) => {
      const payload = JSON.parse(resultJson) as AcapiRunMethodResult
      if (payload.isError) {
        throw new Error(payload.message ?? 'Unknown binding error')
      }
      return payload.result as T
    })
  }

  const hostObject = window.chrome?.webview?.hostObjects.sync[bindingName]
  if (!hostObject) {
    return Promise.reject(
      new Error(`Binding '${bindingName}' is not available - is this page running inside the openBCF WebView2 or ArchiCAD host?`),
    )
  }

  const requestId = `req-${++requestCounter}`
  return new Promise<T>((resolve, reject) => {
    pending.set(requestId, { resolve: resolve as (value: unknown) => void, reject })
    hostObject.RunMethod(methodName, requestId, JSON.stringify(args))
  })
}

/** Subscribes to a C#-pushed event (BrowserBridge.Send). Returns an unsubscribe function. */
export function onBindingEvent(bindingName: string, eventName: string, listener: (payload: unknown) => void): () => void {
  const key = `${bindingName}:${eventName}`
  if (!eventListeners.has(key)) {
    eventListeners.set(key, new Set())
  }
  eventListeners.get(key)!.add(listener)
  return () => eventListeners.get(key)?.delete(listener)
}
