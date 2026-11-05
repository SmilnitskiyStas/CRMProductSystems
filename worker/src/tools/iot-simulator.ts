import { randomUUID } from "node:crypto";
import { parseArgs } from "node:util";
import mqtt from "mqtt";

export type Simulation = "weight" | "temperature" | "low-confidence";
export function simulatedReading(kind: Simulation, device: string, eventId: string, value?: number, at = new Date()): Buffer {
  const common={ device_id:device,event_id:eventId,timestamp:at.toISOString(),battery:95 };
  return Buffer.from(JSON.stringify(kind === "temperature" ? {...common,temperature:value??10,humidity:65}
    : {...common,delta:value??(kind === "low-confidence"?-100:-245)}));
}

export async function publishSimulation(broker: string, topic: string, payload: Buffer, repeat=1): Promise<void> {
  if(!Number.isInteger(repeat)||repeat<1||repeat>100)throw new Error("repeat must be 1..100");
  const client=mqtt.connect(broker,{reconnectPeriod:0,connectTimeout:5000});
  try {
    await new Promise<void>((resolve,reject)=>{client.once("connect",()=>resolve());client.once("error",reject);});
    for(let n=0;n<repeat;n++)await client.publishAsync(topic,payload,{qos:1,retain:false});
  }finally{await client.endAsync(true);}
}

if(require.main===module) {
  const {values}=parseArgs({options:{broker:{type:"string",default:"mqtt://127.0.0.1:1886"},tenant:{type:"string"},location:{type:"string"},zone:{type:"string"},device:{type:"string"},
    kind:{type:"string",default:"weight"},value:{type:"string"},"event-id":{type:"string"},repeat:{type:"string",default:"1"}}});
  const uuid=/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
  if(!values.tenant||!uuid.test(values.tenant)||!values.location||!uuid.test(values.location)||!values.zone||!uuid.test(values.zone)||!values.device
    ||!["weight","temperature","low-confidence"].includes(values.kind!))throw new Error("Provide --tenant UUID --location UUID --zone UUID --device ID --kind weight|temperature|low-confidence");
  const eventId=values["event-id"]??"sim:"+randomUUID();const value=values.value==null?undefined:Number(values.value);
  if(value!==undefined&&!Number.isFinite(value))throw new Error("value must be finite");
  const topic=`shelfguard/${values.tenant.toLowerCase()}/${values.location.toLowerCase()}/${values.zone.toLowerCase()}/shelf`;
  const payload=simulatedReading(values.kind as Simulation,values.device,eventId,value);
  publishSimulation(values.broker!,topic,payload,Number(values.repeat)).then(()=>console.log(JSON.stringify({topic,eventId,published:Number(values.repeat)})),error=>{console.error(error instanceof Error?error.message:error);process.exitCode=1;});
}
