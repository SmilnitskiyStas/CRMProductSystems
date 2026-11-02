import type { IPublishPacket } from "mqtt";
import type { IngestionResult } from "./iot-ingestion";

/** Never acknowledge a retryable database failure or replay a retained sensor state as a delta. */
export function sensorMessageHandler(
  ingest: (topic: string, raw: Buffer) => Promise<IngestionResult>,
  disconnect: (error: Error) => void,
  committed: (result: IngestionResult) => void,
): (packet: IPublishPacket, done: (error?: Error) => void) => void {
  return (packet, done) => {
    if (packet.retain) { done(); return; }
    void ingest(packet.topic, Buffer.isBuffer(packet.payload) ? packet.payload : Buffer.from(packet.payload))
      .then(result => {
        if (result.status === "conflict") console.warn("[mqtt] event identity reused with different content; ignored");
        if (result.status === "ignored") console.warn("[mqtt] invalid or unregistered sensor message ignored");
        committed(result);
        done();
      }, error => {
        const failure = error instanceof Error ? error : new Error("IoT ingestion failed");
        console.error("[mqtt] ingestion failed; reconnecting for redelivery:", failure.message);
        disconnect(failure);
        done(failure);
      });
  };
}
