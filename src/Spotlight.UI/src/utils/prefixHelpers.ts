async function digest(algorithm: "SHA-1" | "SHA-256", message: string): Promise<string> {
  try {
    const hash = await crypto.subtle.digest(algorithm, new TextEncoder().encode(message));
    return Array.from(new Uint8Array(hash))
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
  } catch {
    return `Error generating ${algorithm}`;
  }
}

export const calculateSHA256 = (message: string) => digest("SHA-256", message);
export const calculateSHA1 = (message: string) => digest("SHA-1", message);
