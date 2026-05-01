export const DEFAULT_COLOR_SCHEME = "red-up-green-down";

export const COLOR_SCHEMES = [
  {
    value: "red-up-green-down",
    label: "红涨绿跌 / 红买绿卖",
    colors: {
      up: "#c44536",
      down: "#2f7d4f",
      buy: "#c44536",
      sell: "#2f7d4f",
    },
  },
  {
    value: "green-up-red-down",
    label: "绿涨红跌 / 绿买红卖",
    colors: {
      up: "#2f7d4f",
      down: "#c44536",
      buy: "#2f7d4f",
      sell: "#c44536",
    },
  },
  {
    value: "colorblind",
    label: "色盲友好 / 蓝涨橙跌",
    colors: {
      up: "#0072b2",
      down: "#d55e00",
      buy: "#0072b2",
      sell: "#d55e00",
    },
  },
];

const STORAGE_KEY = "thuai-9-color-scheme";

export function normalizeColorScheme(value) {
  return COLOR_SCHEMES.some((scheme) => scheme.value === value)
    ? value
    : DEFAULT_COLOR_SCHEME;
}

export function getColorScheme(value) {
  const normalized = normalizeColorScheme(value);
  return COLOR_SCHEMES.find((scheme) => scheme.value === normalized) || COLOR_SCHEMES[0];
}

export function loadColorScheme() {
  try {
    return normalizeColorScheme(window.localStorage.getItem(STORAGE_KEY));
  } catch {
    return DEFAULT_COLOR_SCHEME;
  }
}

export function saveColorScheme(value) {
  try {
    window.localStorage.setItem(STORAGE_KEY, normalizeColorScheme(value));
  } catch {
    // Storage can be unavailable under strict browser privacy settings.
  }
}

export function applyColorScheme(value, root = document.documentElement) {
  const scheme = getColorScheme(value);
  root.dataset.colorScheme = scheme.value;
  root.style.setProperty("--market-up", scheme.colors.up);
  root.style.setProperty("--market-down", scheme.colors.down);
  root.style.setProperty("--side-buy", scheme.colors.buy);
  root.style.setProperty("--side-sell", scheme.colors.sell);
}

export function readAppliedPalette(root = document.documentElement) {
  const fallback = getColorScheme(DEFAULT_COLOR_SCHEME).colors;
  const styles = getComputedStyle(root);
  const read = (name, fallbackValue) => styles.getPropertyValue(name).trim() || fallbackValue;

  return {
    up: read("--market-up", fallback.up),
    down: read("--market-down", fallback.down),
    buy: read("--side-buy", fallback.buy),
    sell: read("--side-sell", fallback.sell),
  };
}
