async function sendPluginCommand(event) {
  const client = window.SDPIComponents?.streamDeckClient;
  if (!client?.send) return;
  await client.send("sendToPlugin", { event });
}
