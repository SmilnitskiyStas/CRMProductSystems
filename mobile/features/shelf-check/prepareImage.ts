import * as ImageManipulator from 'expo-image-manipulator';
import type { PreparedPhoto } from './types';

export const SHELF_PHOTO_MAX_SIDE = 1600;
const JPEG_QUALITY = 0.7;

/** Downscales (longest side <= 1600px) and re-encodes as JPEG before upload. */
export async function prepareShelfPhoto(uri: string, width: number, height: number): Promise<PreparedPhoto> {
  const ctx = ImageManipulator.ImageManipulator.manipulate(uri);
  if (Math.max(width, height) > SHELF_PHOTO_MAX_SIDE) {
    ctx.resize(width >= height ? { width: SHELF_PHOTO_MAX_SIDE } : { height: SHELF_PHOTO_MAX_SIDE });
  }
  const rendered = await ctx.renderAsync();
  const saved = await rendered.saveAsync({
    format: ImageManipulator.SaveFormat.JPEG,
    compress: JPEG_QUALITY,
  });
  return { uri: saved.uri, width: saved.width, height: saved.height };
}
