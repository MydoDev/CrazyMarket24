import * as signalR from "@microsoft/signalr";
import { API_BASE_URL } from "./api";

export type StoreClientGroup = "kiosk" | "admin" | "simulator";

export function createStoreHubConnection(
  clientGroup: StoreClientGroup = "kiosk"
): signalR.HubConnection {
  return new signalR.HubConnectionBuilder()
    .withUrl(`${API_BASE_URL}/hubs/store?client=${clientGroup}`, {
      withCredentials: true,
    })
    .withAutomaticReconnect([0, 1000, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();
}
