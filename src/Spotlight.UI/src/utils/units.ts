/** Table-driven unit converter: "10 ft to m", "5 km in miles", "72 f to c", "2.5 gb to mb". */

interface Unit {
  category: string;
  /** Multiplier to the category's base unit. */
  factor: number;
  label: string;
}

const U: Record<string, Unit> = {};

function add(category: string, label: string, factor: number, aliases: string[]) {
  const unit: Unit = { category, factor, label };
  for (const a of [label.toLowerCase(), ...aliases]) U[a] = unit;
}

// length (base: metre)
add("length", "mm", 0.001, ["millimeter", "millimeters", "millimetre", "millimetres"]);
add("length", "cm", 0.01, ["centimeter", "centimeters", "centimetre", "centimetres"]);
add("length", "m", 1, ["meter", "meters", "metre", "metres"]);
add("length", "km", 1000, ["kilometer", "kilometers", "kilometre", "kilometres"]);
add("length", "in", 0.0254, ["inch", "inches", '"']);
add("length", "ft", 0.3048, ["foot", "feet"]);
add("length", "yd", 0.9144, ["yard", "yards"]);
add("length", "mi", 1609.344, ["mile", "miles"]);
add("length", "nmi", 1852, ["nauticalmile", "nauticalmiles"]);

// mass (base: kilogram)
add("mass", "mg", 1e-6, ["milligram", "milligrams"]);
add("mass", "g", 0.001, ["gram", "grams"]);
add("mass", "kg", 1, ["kilogram", "kilograms", "kilo", "kilos"]);
add("mass", "t", 1000, ["tonne", "tonnes", "metricton"]);
add("mass", "oz", 0.028349523125, ["ounce", "ounces"]);
add("mass", "lb", 0.45359237, ["lbs", "pound", "pounds"]);
add("mass", "st", 6.35029318, ["stone", "stones"]);

// volume (base: litre, US units)
add("volume", "ml", 0.001, ["milliliter", "milliliters", "millilitre", "millilitres"]);
add("volume", "l", 1, ["liter", "liters", "litre", "litres"]);
add("volume", "tsp", 0.00492892159375, ["teaspoon", "teaspoons"]);
add("volume", "tbsp", 0.01478676478125, ["tablespoon", "tablespoons"]);
add("volume", "fl oz", 0.0295735295625, ["floz", "fluidounce", "fluidounces"]);
add("volume", "cup", 0.2365882365, ["cups"]);
add("volume", "pt", 0.473176473, ["pint", "pints"]);
add("volume", "qt", 0.946352946, ["quart", "quarts"]);
add("volume", "gal", 3.785411784, ["gallon", "gallons"]);

// speed (base: m/s)
add("speed", "m/s", 1, ["mps"]);
add("speed", "km/h", 1 / 3.6, ["kmh", "kph", "kmph"]);
add("speed", "mph", 0.44704, ["mi/h"]);
add("speed", "kn", 0.514444, ["knot", "knots", "kt"]);
add("speed", "ft/s", 0.3048, ["fps"]);

// data (base: byte, binary multiples like Windows reports them)
add("data", "bit", 1 / 8, ["bits"]);
add("data", "B", 1, ["byte", "bytes"]);
add("data", "KB", 1024, ["kb", "kib", "kilobyte", "kilobytes"]);
add("data", "MB", 1024 ** 2, ["mb", "mib", "megabyte", "megabytes"]);
add("data", "GB", 1024 ** 3, ["gb", "gib", "gigabyte", "gigabytes"]);
add("data", "TB", 1024 ** 4, ["tb", "tib", "terabyte", "terabytes"]);

// time (base: second)
add("time", "ms", 0.001, ["millisecond", "milliseconds"]);
add("time", "s", 1, ["sec", "secs", "second", "seconds"]);
add("time", "min", 60, ["mins", "minute", "minutes"]);
add("time", "h", 3600, ["hr", "hrs", "hour", "hours"]);
add("time", "d", 86400, ["day", "days"]);
add("time", "wk", 604800, ["week", "weeks"]);
add("time", "yr", 31557600, ["year", "years"]);

// area (base: square metre)
add("area", "m²", 1, ["m2", "sqm", "squaremeter", "squaremeters"]);
add("area", "km²", 1e6, ["km2", "sqkm"]);
add("area", "ft²", 0.09290304, ["ft2", "sqft", "squarefoot", "squarefeet"]);
add("area", "yd²", 0.83612736, ["yd2", "sqyd"]);
add("area", "ha", 10000, ["hectare", "hectares"]);
add("area", "acre", 4046.8564224, ["acres"]);

// temperature handled separately
const TEMP: Record<string, string> = {
  c: "c", "°c": "c", celsius: "c", centigrade: "c",
  f: "f", "°f": "f", fahrenheit: "f",
  k: "k", kelvin: "k",
};

function toCelsius(v: number, unit: string): number {
  return unit === "c" ? v : unit === "f" ? ((v - 32) * 5) / 9 : v - 273.15;
}
function fromCelsius(c: number, unit: string): number {
  return unit === "c" ? c : unit === "f" ? (c * 9) / 5 + 32 : c + 273.15;
}

function fmt(n: number): string {
  if (n === 0) return "0";
  const abs = Math.abs(n);
  if (abs >= 1e12 || abs < 1e-6) return n.toExponential(4);
  const rounded = parseFloat(n.toPrecision(8));
  return rounded.toLocaleString("en-US", { maximumFractionDigits: 8, useGrouping: abs >= 10000 });
}

const PATTERN = /^\s*([+-]?\d+(?:[.,]\d+)*(?:e[+-]?\d+)?)\s*([a-z°²/"0-9 ]*?)\s+(?:to|in|into|as|->|=)\s+([a-z°²/0-9 ]+?)\s*$/i;

export interface Conversion {
  result: string;
  copy: string;
  expression: string;
}

export function convertUnits(query: string): Conversion | null {
  const m = query.match(PATTERN);
  if (!m) return null;

  const value = parseFloat(m[1].replace(/,/g, ""));
  const from = m[2].trim().toLowerCase();
  const to = m[3].trim().toLowerCase();
  if (Number.isNaN(value) || !from || !to) return null;

  const ft = TEMP[from];
  const tt = TEMP[to];
  if (ft && tt) {
    const out = fromCelsius(toCelsius(value, ft), tt);
    const label = tt === "k" ? "K" : `°${tt.toUpperCase()}`;
    return { result: `${fmt(out)} ${label}`, copy: fmt(out).replace(/,/g, ""), expression: `${fmt(value)} ${ft === "k" ? "K" : "°" + ft.toUpperCase()}` };
  }

  const fu = U[from] ?? U[from.replace(/\s+/g, "")];
  const tu = U[to] ?? U[to.replace(/\s+/g, "")];
  if (!fu || !tu || fu.category !== tu.category) return null;

  const out = (value * fu.factor) / tu.factor;
  return { result: `${fmt(out)} ${tu.label}`, copy: String(parseFloat(out.toPrecision(10))), expression: `${fmt(value)} ${fu.label}` };
}
