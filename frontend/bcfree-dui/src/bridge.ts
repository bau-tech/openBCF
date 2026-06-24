// Thin wrapper around the WebView2 host-object bridge set up by BCFree.Dui's BrowserBridge (see
// src/BCFree.Dui/Bridge/BrowserBridge.cs). Each C# IBinding is exposed here as
// `chrome.webview.hostObjects.sync.<bindingName>`, with exactly one method, RunMethod, that we
// call to kick off work; the (possibly async) result comes back later through
// window.__bcfreeDuiReceiveResult, keyed by requestId, and is resolved against the matching
// pending Promise below.

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (reason: unknown) => void
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
    __bcfreeDuiReceiveResult?: (requestId: string, isError: boolean, payloadJson: string) => void
    __bcfreeDuiReceiveEvent?: (bindingName: string, eventName: string, payloadJson: string) => void
  }
}

const pending = new Map<string, PendingRequest>()
const eventListeners = new Map<string, Set<(payload: unknown) => void>>()
let requestCounter = 0

window.__bcfreeDuiReceiveResult = (requestId, isError, payloadJson) => {
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

window.__bcfreeDuiReceiveEvent = (bindingName, eventName, payloadJson) => {
  const listeners = eventListeners.get(`${bindingName}:${eventName}`)
  if (!listeners) {
    return
  }
  const payload = JSON.parse(payloadJson)
  listeners.forEach((listener) => listener(payload))
}

/** Calls a method on a C# IBinding and resolves with its (JSON-decoded) return value. */
export function callBinding<T = unknown>(bindingName: string, methodName: string, ...args: unknown[]): Promise<T> {
  const hostObject = window.chrome?.webview?.hostObjects.sync[bindingName]
  if (!hostObject) {
    return Promise.reject(
      new Error(`Binding '${bindingName}' is not available - is this page running inside the BCFree WebView2 host?`),
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
