import { api } from "@/lib/api";
import type { BlockDefinitionDto } from "../types";

const BLOCKS_PATH = "/api/v1/mobile/blocks";

/** GET /api/v1/mobile/blocks — every registered block type, in catalog order. Not tenant-scoped. */
export async function fetchBlockRegistry(): Promise<BlockDefinitionDto[]> {
  return api.get<BlockDefinitionDto[]>(BLOCKS_PATH);
}
