export function convertUnits(query: string): string | null {
  const regex = /^\s*([+-]?\d+(?:\.\d+)?)\s*([a-zA-Z°]+)\s+to\s+([a-zA-Z°]+)\s*$/i;
  const match = query.match(regex);
  if (!match) return null;

  const value = parseFloat(match[1]);
  const from = match[2].toLowerCase();
  const to = match[3].toLowerCase();

  // Length
  if ((from === "ft" || from === "feet") && to === "m") return `${(value * 0.3048).toFixed(3)} m`;
  if (from === "m" && (to === "ft" || to === "feet")) return `${(value / 0.3048).toFixed(3)} ft`;
  if ((from === "in" || from === "inch" || from === "inches") && to === "cm") return `${(value * 2.54).toFixed(3)} cm`;
  if (from === "cm" && (to === "in" || to === "inch" || to === "inches")) return `${(value / 2.54).toFixed(3)} in`;
  if (from === "km" && (to === "mi" || to === "mile" || to === "miles")) return `${(value * 0.621371).toFixed(3)} miles`;
  if ((from === "mi" || from === "mile" || from === "miles") && to === "km") return `${(value / 0.621371).toFixed(3)} km`;
  
  // Weight
  if ((from === "kg" || from === "kilogram" || from === "kilograms") && (to === "lb" || to === "lbs" || to === "pound" || to === "pounds")) return `${(value * 2.20462).toFixed(3)} lbs`;
  if ((from === "lb" || from === "lbs" || from === "pound" || from === "pounds") && (to === "kg" || to === "kilogram" || to === "kilograms")) return `${(value / 2.20462).toFixed(3)} kg`;
  
  // Temperature
  if (from === "f" && to === "c") return `${((value - 32) * 5 / 9).toFixed(1)} °C`;
  if (from === "c" && to === "f") return `${((value * 9 / 5) + 32).toFixed(1)} °F`;

  return null;
}

export async function calculateSHA256(message: string): Promise<string> {
  try {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-256', msgBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
  } catch {
    return "Error generating SHA-256";
  }
}

export async function calculateSHA1(message: string): Promise<string> {
  try {
    const msgBuffer = new TextEncoder().encode(message);
    const hashBuffer = await crypto.subtle.digest('SHA-1', msgBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map(b => b.toString(16).padStart(2, '0')).join('');
  } catch {
    return "Error generating SHA-1";
  }
}
