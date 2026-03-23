# Design System Document: The Technical Studio

## 1. Overview & Creative North Star

**Creative North Star: The Architectural Monolith**
This design system moves away from the ephemeral nature of web-based SaaS and leans into the permanence of a professional Windows desktop environment. It is inspired by architectural blueprints and high-end studio hardware - precise, dependable, and grounded. We reject "airy" mobile-first minimalism in favor of **Intentional Density**.

The interface should feel like a custom-milled tool. We achieve this through "The Technical Studio" aesthetic: a high-contrast relationship between deep forest grounding elements and crisp mist surfaces, separated not by shadows, but by structural 1px "technical lines" and inset "well" patterns that imply a physical housing for data.

---

## 2. Colors & Surface Logic

The palette is engineered to convey privacy and local-first reliability through solid, opaque surfaces.

### Surface Hierarchy & Nesting
Instead of floating cards, we use **Tonal Nesting**. The UI is treated as a single block of material where "wells" are carved out to hold information.

* **The "No-Shadow" Rule:** Do not use drop shadows to create depth. Depth is strictly two-dimensional, achieved via the **Outline (#737874)** or **Outline Variant (#C2C8C3)**.
* **The Nesting Order:**
  1. **Base Layer:** `surface` (#F8FAF9) for the primary application window.
  2. **Structural Sections:** `surface-container-low` (#F2F4F3) to define large sidebars or utility panels.
  3. **Data Wells:** `surface-container-high` (#E6E9E8) for recording lists or transcript areas. Use a `1px` inner border of `outline-variant` to create the "inset" feel.

### Key Palettes
* **Primary (Grounding):** `primary-container` (#1B2B24). Use this for high-authority headers or "Control Centers."
* **Secondary (Utility):** `secondary` (#47626F). Used for persistent technical actions that are not the main recording flow.
* **Active State (Signal):** `on-tertiary-fixed-variant` (#0E5138). Specifically for the "Recording Live" indicators.
* **Alert/Update:** `Alert Amber` (for model updates/technical warnings).

---

## 3. Typography

The system utilizes a dual-font strategy to separate the "Human Interface" from the "Machine Data," while relying only on fonts expected to be available on supported Windows systems. Do not package or ship custom font assets for this product.

* **Segoe UI (UI & Interaction):** Used for all labels, headings, and navigation. It provides a clean, neutral tone that stays out of the way of the user's work and is available across supported Windows environments.
* **Cascadia Mono with Consolas fallback (Technical Metadata):** Used for timestamps, version numbers, file sizes, and bitrates. This mono pairing reinforces the "Architectural" theme, signaling to the user that they are looking at raw, precise data without introducing packaged font dependencies.

### Typographic Scale
* **Headline-SM (Segoe UI, 1.5rem):** Used for section titles (for example, "Library" or "Settings").
* **Title-SM (Segoe UI, 1rem, Bold):** Used for meeting titles within the list.
* **Label-MD (Segoe UI, 0.75rem):** All UI buttons and field labels.
* **Technical-MD (Cascadia Mono / Consolas, 0.875rem):** Timestamps (for example, `00:14:52`) and technical logs.

---

## 4. Elevation & Depth: The "Technical Line"

In this design system, we do not "elevate" objects; we "mill" them into the surface.

* **The Layering Principle:** Use the `surface-container` tiers to create hierarchy. A "High" tier container sitting inside a "Low" tier surface creates a natural visual recession.
* **The Ghost Border:** For accessibility, use a `1px` border of `outline-variant` at 20% opacity. This provides a "technical edge" without the bulk of a standard UI border.
* **Inset Wells:** To create a data "well," apply a `1px` top border of `outline` and a `1px` side border of `outline-variant`. This mimics the way light hits a recessed physical groove.

---

## 5. Components

### Buttons
* **Primary Action (Recording):** `primary-container` (#1B2B24) with `on-primary` text. Tight `4px` radius. No gradients.
* **Secondary (Technical):** `secondary` (#47626F). Used for "Export," "Edit," or "Share."
* **Ghost/Tertiary:** No background, `outline` 1px border. Used for low-priority technical toggles.

### The "Recording" Chip
A signature component for this app. Use a `surface-container-lowest` background with a `2px` stroke of `Signal Green (#2D6A4F)`. The text inside must use `Cascadia Mono` with `Consolas` fallback to emphasize the live data stream.

### Input Fields & Technical "Wells"
* **Fields:** Background `surface-container-lowest`, `1px` border of `outline-variant`.
* **Data Areas:** For the transcript or meeting notes, use an "Inset Well" pattern. Background: `surface-container-high`. No shadows. High information density - reduce line height to `1.2` for transcriptions to maximize visible data.

### Iconography
* **Style:** Monochromatic, `2px` stroke.
* **Strict Rule:** Icons must never be filled unless they are in an "Active/Selected" state. Use geometric, sharp-edged icons to match the `4px` border radius of the containers.

---

## 6. Do's and Don'ts

### Do
* **Do** use `Cascadia Mono` with `Consolas` fallback for any number that is being updated in real time.
* **Do** use `1px` lines to separate headers from content.
* **Do** embrace high density. On a professional desktop app, users value seeing more information at once over "breathing room."
* **Do** keep all surfaces opaque. This is a local-first app; transparency can imply a lack of "solidity" or "privacy."

### Don't
* **Don't** use a border radius larger than `4px`. Anything softer breaks the "Architectural" feel.
* **Don't** use standard drop shadows. Use tonal shifts in background color instead.
* **Don't** use mobile-style toggle switches. Use technical checkboxes or segmented button groups.
* **Don't** use dividers between list items. Instead, use `4px` (0.9rem) vertical space or a subtle shift from `surface-container-low` to `surface-container-high`.

---

## 7. Spacing Scale

We utilize a tight, `0.2rem`-based increment system to maintain high density.

* **Tight (1 - 0.2rem):** Between related labels and inputs.
* **Standard (2.5 - 0.5rem):** Internal padding for buttons and chips.
* **Section (4 - 0.9rem):** Margin between major "Wells" or functional groups.
* **Edge (8 - 1.75rem):** Maximum padding for the outer application frame.
