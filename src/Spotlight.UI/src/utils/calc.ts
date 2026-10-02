/**
 * Small recursive-descent calculator. Replaces `new Function(...)`, which executed whatever the user typed.
 *
 * Supports + - * / ^ ** %, parentheses, unary minus, constants (pi, e), functions (sqrt, sin, log, ...),
 * percentages ("20% of 150", "100 + 10%") and thousands separators ("1,000 * 3").
 */

const FUNCTIONS: Record<string, (...args: number[]) => number> = {
  sqrt: Math.sqrt,
  cbrt: Math.cbrt,
  abs: Math.abs,
  sin: Math.sin,
  cos: Math.cos,
  tan: Math.tan,
  asin: Math.asin,
  acos: Math.acos,
  atan: Math.atan,
  ln: Math.log,
  log: Math.log10,
  log2: Math.log2,
  exp: Math.exp,
  round: Math.round,
  floor: Math.floor,
  ceil: Math.ceil,
  min: Math.min,
  max: Math.max,
  pow: Math.pow,
  fact: (n) => {
    if (n < 0 || n > 170 || !Number.isInteger(n)) return NaN;
    let r = 1;
    for (let i = 2; i <= n; i++) r *= i;
    return r;
  },
};

const CONSTANTS: Record<string, number> = { pi: Math.PI, e: Math.E, tau: Math.PI * 2 };

type Token =
  | { t: "num"; v: number }
  | { t: "id"; v: string }
  | { t: "op"; v: string };

function tokenize(input: string): Token[] | null {
  const tokens: Token[] = [];
  let i = 0;
  while (i < input.length) {
    const c = input[i];
    if (/\s/.test(c)) { i++; continue; }

    if (/[0-9.]/.test(c)) {
      let j = i;
      while (j < input.length && /[0-9.,]/.test(input[j])) j++;
      const text = input.slice(i, j).replace(/,(?=\d{3}(\D|$))/g, "");
      if (/,/.test(text)) return null;
      const v = Number(text);
      if (Number.isNaN(v)) return null;
      tokens.push({ t: "num", v });
      i = j;
      continue;
    }

    if (/[a-z]/i.test(c)) {
      let j = i;
      while (j < input.length && /[a-z0-9]/i.test(input[j])) j++;
      tokens.push({ t: "id", v: input.slice(i, j).toLowerCase() });
      i = j;
      continue;
    }

    if (c === "*" && input[i + 1] === "*") { tokens.push({ t: "op", v: "^" }); i += 2; continue; }
    if ("+-*/^%(),×÷−".includes(c)) {
      tokens.push({ t: "op", v: c === "×" ? "*" : c === "÷" ? "/" : c === "−" ? "-" : c });
      i++;
      continue;
    }
    return null;
  }
  return tokens;
}

class Parser {
  private pos = 0;
  private tokens: Token[];
  constructor(tokens: Token[]) {
    this.tokens = tokens;
  }

  parse(): number {
    const v = this.expression();
    if (this.pos < this.tokens.length) throw new Error("Unexpected token");
    return v;
  }

  private peek(): Token | undefined { return this.tokens[this.pos]; }
  private isOp(v: string): boolean { const p = this.peek(); return !!p && p.t === "op" && p.v === v; }

  private expression(): number {
    let left = this.term();
    for (;;) {
      if (this.isOp("+")) {
        this.pos++;
        const right = this.term();
        // "100 + 10%" means 100 + 10% of 100
        left += this.endedWithPercent ? left * right : right;
      } else if (this.isOp("-")) {
        this.pos++;
        const right = this.term();
        left -= this.endedWithPercent ? left * right : right;
      } else return left;
    }
  }

  /** True when the operand that was just parsed was written as a percentage ("10%"). */
  private endedWithPercent = false;

  private term(): number {
    let left = this.unary();
    for (;;) {
      if (this.isOp("*")) { this.pos++; left *= this.unary(); }
      else if (this.isOp("/")) { this.pos++; left /= this.unary(); }
      else if (this.isOp("%") && this.startsOperand(this.tokens[this.pos + 1])) {
        this.pos++;
        left %= this.unary();
      } else return left;
    }
  }

  private startsOperand(t: Token | undefined): boolean {
    return !!t && (t.t === "num" || t.t === "id" || (t.t === "op" && (t.v === "(" || t.v === "-")));
  }

  // Exponentiation binds tighter than unary minus (-3^2 = -9) and is right-associative (2^3^2 = 2^9).
  private power(): number {
    const base = this.postfix();
    if (this.isOp("^")) { this.pos++; return Math.pow(base, this.unary()); }
    return base;
  }

  private unary(): number {
    this.endedWithPercent = false;
    if (this.isOp("-")) { this.pos++; return -this.unary(); }
    if (this.isOp("+")) { this.pos++; return this.unary(); }
    return this.power();
  }

  private postfix(): number {
    let v = this.primary();
    // trailing "%" (not followed by an operand) is a percentage: 20% -> 0.2
    while (this.isOp("%") && !this.startsOperand(this.tokens[this.pos + 1])) {
      this.pos++;
      v /= 100;
      this.endedWithPercent = true;
    }
    return v;
  }

  private primary(): number {
    const tok = this.tokens[this.pos++];
    if (!tok) throw new Error("Unexpected end");

    if (tok.t === "num") return tok.v;

    if (tok.t === "id") {
      if (tok.v in CONSTANTS) return CONSTANTS[tok.v];
      const fn = FUNCTIONS[tok.v];
      if (!fn) throw new Error("Unknown identifier " + tok.v);
      if (!this.isOp("(")) throw new Error("Expected (");
      this.pos++;
      const args: number[] = [];
      if (!this.isOp(")")) {
        do { args.push(this.expression()); } while (this.isOp(",") && ++this.pos);
      }
      if (!this.isOp(")")) throw new Error("Expected )");
      this.pos++;
      return fn(...args);
    }

    if (tok.v === "(") {
      const v = this.expression();
      if (!this.isOp(")")) throw new Error("Expected )");
      this.pos++;
      return v;
    }
    throw new Error("Unexpected " + tok.v);
  }
}

export function evaluate(expression: string): number | null {
  let expr = expression.trim();
  if (!expr) return null;

  // "20% of 150" -> (20/100)*(150)
  expr = expr.replace(/(\d+(?:\.\d+)?)\s*%\s*of\s+/gi, "($1/100)*");

  const tokens = tokenize(expr);
  if (!tokens || tokens.length === 0) return null;

  try {
    const value = new Parser(tokens).parse();
    return typeof value === "number" && Number.isFinite(value) ? value : null;
  } catch {
    return null;
  }
}

export function formatNumber(value: number): string {
  if (Number.isInteger(value) && Math.abs(value) < 1e21) return String(value);
  const rounded = parseFloat(value.toPrecision(12));
  return String(rounded);
}

/** True when the text looks like arithmetic rather than a search term. */
export function looksLikeMath(query: string): boolean {
  const q = query.trim();
  if (!/\d/.test(q) && !/^(pi|e|tau)$/i.test(q)) return false;
  if (/^[\d\s+\-*/().,^%×÷−]+$/.test(q)) return /[+\-*/^%×÷−]|\(/.test(q) && !/^[\d\s.,]+$/.test(q);
  return /^[\d\s+\-*/().,^%a-z]+$/i.test(q) && (/(sqrt|cbrt|abs|sin|cos|tan|ln|log|exp|round|floor|ceil|min|max|pow|fact|pi)\b/i.test(q) || /\d+\s*%\s*of\s+\d/i.test(q));
}
