export interface NetworkReachability {
  isConnected: boolean | null;
  isInternetReachable: boolean | null;
}

export function isPosOffline(network: NetworkReachability): boolean {
  return network.isConnected === false || network.isInternetReachable === false;
}
