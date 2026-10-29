/** Handle to the Blazor component, used for JS-to-.NET callbacks. */
export interface DotNetRef {
  invokeMethodAsync(method: string, ...args: unknown[]): Promise<unknown>;
}
