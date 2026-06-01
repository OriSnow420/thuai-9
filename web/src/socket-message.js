const DEFAULT_ERROR_PREVIEW_LIMIT = 240;

export async function readSocketPayload(data) {
  if (typeof data === "string") {
    return data;
  }

  if (data instanceof ArrayBuffer) {
    return new TextDecoder().decode(new Uint8Array(data));
  }

  if (ArrayBuffer.isView(data)) {
    return new TextDecoder().decode(data);
  }

  if (typeof Blob !== "undefined" && data instanceof Blob) {
    return data.text();
  }

  return String(data ?? "");
}

export async function parseSocketMessageData(data) {
  const raw = await readSocketPayload(data);
  return {
    raw,
    message: JSON.parse(raw),
  };
}

export function previewSocketPayload(raw, limit = DEFAULT_ERROR_PREVIEW_LIMIT) {
  const normalized = String(raw ?? "").replace(/\s+/g, " ").trim();
  if (!normalized) return "";
  if (normalized.length <= limit) return normalized;
  return `${normalized.slice(0, Math.max(0, limit - 1))}...`;
}
