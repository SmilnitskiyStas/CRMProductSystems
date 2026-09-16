import Constants from "expo-constants";
import * as Device from "expo-device";
import * as Notifications from "expo-notifications";
import { Platform } from "react-native";
import { personalApiClient } from "@/lib/api-client";

Notifications.setNotificationHandler({
  handleNotification: async () => ({ shouldShowBanner: true, shouldShowList: true, shouldPlaySound: true, shouldSetBadge: false }),
});

export async function registerConsumerPushNotifications(): Promise<void> {
  if (!Device.isDevice) return;
  const current = await Notifications.getPermissionsAsync();
  const status = current.status === "granted" ? current.status : (await Notifications.requestPermissionsAsync()).status;
  if (status !== "granted") return;
  if (Platform.OS === "android") await Notifications.setNotificationChannelAsync("default", {
    name: "Основні повідомлення", importance: Notifications.AndroidImportance.DEFAULT,
  });
  const projectId = Constants.expoConfig?.extra?.eas?.projectId ?? Constants.easConfig?.projectId;
  if (!projectId) return;
  const token = (await Notifications.getExpoPushTokenAsync({ projectId })).data;
  await personalApiClient.put("/consumer/push-token", { token });
}
