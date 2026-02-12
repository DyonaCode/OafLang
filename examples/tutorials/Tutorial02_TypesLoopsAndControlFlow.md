# Tutorial 02: Types, Loops, and Control Flow

## Goal

Use mutable state, typed declarations, casts, and loop control.

## Source

```oaf
float price = 19.99;
int rounded = (int)price;

flux sum = 0;
flux i = rounded;
loop i > 0 => {
    if i == 3 => {
        i -= 1;
        continue;
    }
    sum += i;
    i -= 1;
}

return sum;
```

## Run

```bash
oaf run "float price = 19.99; int rounded = (int)price; flux sum = 0; flux i = rounded; loop i > 0 => { if i == 3 => { i -= 1; continue; } sum += i; i -= 1; } return sum;"
```

## Notes

- `flux` enables mutation.
- Explicit numeric cast `(int)price` is required for narrowing conversion.
- Use `{ ... }` for multi-statement loop and branch bodies.
