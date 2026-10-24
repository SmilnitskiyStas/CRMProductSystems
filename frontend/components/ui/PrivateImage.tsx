"use client";

import { useEffect, useState, type ImgHTMLAttributes } from "react";
import { api, getToken } from "@/lib/api";
import { privateAssetPath } from "@/lib/privateAssets";

/** Private uploads are loaded with header auth; credentials never appear in image URLs. */
export function PrivateImage({ src, alt, ...props }: Omit<ImgHTMLAttributes<HTMLImageElement>, "src"> & { src: string }) {
  const token = getToken();
  const [image, setImage] = useState<{ source: string; token: string | null; url: string } | null>(null);
  useEffect(() => {
    const path = privateAssetPath(src);
    if (!path || !token) return;
    const controller = new AbortController();
    let objectUrl: string | undefined;
    let disposed = false;
    void api.getBlob(path, { signal: controller.signal, cache: "no-store" }).then((blob) => {
      if (disposed) return;
      objectUrl = URL.createObjectURL(blob);
      setImage({ source: src, token, url: objectUrl });
    }).catch(() => { /* Leave the image unavailable; never fall back to public static delivery. */ });
    return () => {
      disposed = true;
      controller.abort();
      if (objectUrl) URL.revokeObjectURL(objectUrl);
    };
  }, [src, token]);
  const visibleUrl = image?.source === src && image.token === token ? image.url : undefined;
  // eslint-disable-next-line @next/next/no-img-element
  return <img {...props} src={visibleUrl} alt={alt} />;
}
