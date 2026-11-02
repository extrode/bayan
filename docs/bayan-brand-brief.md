# Bayan: brand brief (draft)

The identity has three parts that share one visual language, the strokes and dots of written Arabic: the **mark** (icon), the **flat hoopoe** (everyday mascot) and the **calligraphic hoopoe** (signature image). The hoopoe is the **only character**; the reed pen (qalam) appears as an object, never as a character.

## 1. The system

| Element | Use | Status |
|---|---|---|
| Mark: the rasm of بين with its four dots | App icon, favicon, `.byn` file badge, social avatar | Sketched (`bayan-icon-sketch.svg`, variations in `bayan-variations-poses-sketch.svg`) |
| Favicon: the mark redrawn on a 32-unit grid | Browser tabs, 16–32 px | Sketched (`bayan-favicon-mascot-sketch.svg`) |
| Wordmark: "Bayan" with بيان | Website header, README, slides | Sketched |
| Flat hoopoe | Docs, error pages, test output, loading screens | Sketched, three poses (`bayan-hoopoe-calligraphy-poses-sketch.svg`) |
| Calligraphic hoopoe | Website hero, release banners, README header | Concept only; needs a calligrapher (section 5) |
| Qalam (reed pen), as an object | Docs headers, the formatter, the prose view; held by the hoopoe or writing the rasm stroke. **No face.** | Character sketch retired; object version to draw |

## 2. The mark

- One continuous stroke: the connected skeleton of بين (ب‑ي‑ن, the root letters and the `.byn` extension), read right to left.
- Four dots, as they fall on the letters: one above ن, two below ي, one below ب.
- Dots are **diamonds** (the cut of a reed pen) at large sizes and **squares** in the favicon so they stay separate.
- Recommended weight: variation B (heavier stroke) with variation A's diamond dots, spaced slightly wider. One mark could then cover every size except the smallest favicon.
- Meaning to keep in all copy: the dots (*iʿjām*) were added to Arabic script to remove ambiguity. Bayan's promise is the same: one form, one meaning.

## 3. Colour

| Role | Name | Hex | Contrast on its background |
|---|---|---|---|
| Ink (primary), text | Navy | `#1F3A5F` | 10.2:1 on parchment |
| Dots, accents (decorative only) | Gold | `#C08A2E` | **2.7:1 on parchment: never use for text** |
| Gold-coloured text on light backgrounds | Text gold | `#8A5F12` | 5.0:1 on parchment |
| Dots and accent text on dark backgrounds | Light gold | `#E0AE55` | 8.1:1 on night |
| Background | Parchment | `#F6F1E7` | |
| Dark background | Night | `#14202E` | |
| Ink and text on dark backgrounds | Cream | `#EDE6D6` | 13.2:1 on night |
| Hoopoe plumage | Sand | `#D9A05B` | Decorative only |
| Lines, legs, outlines | Bronze | `#8A6F3A` | 4.2:1 on parchment: large text only |

**Contrast rule:** text must meet WCAG AA (4.5:1, or 3:1 for text 24 px and larger). Gold, sand and bronze are for shapes and decoration; use navy, cream or text gold for words.

Alternative palette (variation D), if navy and gold feel too formal: teal `#0E5E54` with terracotta `#C25B3A`.

## 4. The hoopoe (hudhud)

**Why the hoopoe:** in Sūrat an-Naml (27:20–22) the hudhud returns to Sulaymān with news it calls certain and clear. That is what Bayan's compiler messages aim to be: exact about what went wrong, where, why and how to fix it.

**Rules (fixed):**

1. **No face.** No eyes, beak, nose or mouth, in any pose or medium, including animations, stickers and merchandise. The head is a plain rounded form.
2. The bird is recognised by its **fan crest with dark-tipped feathers**, its **banded wings** and its **banded tail**. Every pose keeps all three visible.
3. It faces **left**, the direction Arabic is read, unless a layout forces otherwise.
4. Flat style only: solid shapes, no gradients, no outlines except legs.
5. Islamic geometric framing (the eight-pointed star) is optional and decorative; never use religious text as decoration.
6. The reed pen may appear with the hoopoe (held, or writing the rasm stroke) but is always an object: no face, no limbs.

**Poses so far:** delivering an error note, perched on a passing-tests check, in flight with a trail of dots (loading). Candidates: reading a scroll (documentation), standing on a package box (registry), resting (empty states).

**Known consideration:** the hoopoe is also Israel's national bird, so some audiences may read a political meaning into it. Accepted as a known trade-off.

## 5. Brief for a calligrapher: the calligraphic hoopoe

**Goal:** a hoopoe composed entirely from the letters of بيان, in the tradition of zoomorphic calligraphy, for use as Bayan's signature image.

**Fixed elements (please keep):**

- The **bowl of ن** forms the body.
- The word's **four dots** remain visible and countable, placed as the crest tips (they may also echo the mark's diamond shape).
- **No face** of any kind (section 4, rule 1).
- The result must read as a hoopoe at a glance: crest, body, tail.

**Free elements (your judgement):** script style (Thuluth, Diwani or another), stroke weights, which letter forms the neck, wing and tail, overall rhythm and proportion.

**Starting point:** the concept in `bayan-hoopoe-calligraphy-poses-sketch.svg` (alif as neck, swash of ي as wing, teeth of ب and ي as crest, the end of the bowl as tail). It shows composition only, not craft.

**Deliverables:**

- Vector artwork (SVG or AI), with the dots as separate shapes so they can be recoloured.
- One-colour version (navy on parchment, cream on night), plus a version usable at 512 px and at banner width.
- Licence permitting use in an open-source project's identity, including modification of colour and scale.

## 6. Usage summary

| Context | Use |
|---|---|
| Favicon, file icon, small UI | Mark (favicon version below 32 px) |
| App icon, avatar | Mark on parchment or night tile |
| README header, website hero, release posts | Calligraphic hoopoe + wordmark |
| Documentation, error pages, test reports, loading | Flat hoopoe poses |
| Print, stamps, single-colour | Single-colour mark |

## 7. Next steps

- [ ] Finalise the mark (B weight, A dots, wider spacing) and export SVG + PNG sizes (16, 32, 64, 128, 256, 512, 1024).
- [ ] Commission the calligraphic hoopoe (section 5).
- [ ] Add 2–3 more flat poses (documentation, registry, empty state).
- [ ] Check the name and assets against existing marks (trademark search) before the repository goes public.
- [x] Terminal style: see `bayan-terminal-style.md`.
- [ ] Editor theme and TextMate grammar for `.byn`, from the same palette.
- [ ] Cultural review of the calligraphy, the hoopoe and the Qur'anic references before going public.
