# FXB Viewer

The **FXB Viewer** page lets you browse the contents of `.fxb` (FX Bank) files binary packages that define visual effects used throughout the game, such as particle systems, weather effects, and environmental FX.

---

## What is an .fxb file?

An `.fxb` file is a compiled **FX Bank** (`FxBk` format). It is a self-contained binary package built by the game's internal FX authoring tool (FX Studio). Each bank contains a collection of named **effects**, where each effect is composed of **phases**, and each phase is made up of **components** with their own **properties**.

The binary format carries a header (`FxBk` magic word), followed by a series of offset tables pointing to the string table, vector tables, channel data, effect definitions, component definitions, and more. All data is stored in a single flat binary buffer with internal offsets.

Notable game files that use this format include:

- `game:\media\FXParticles\ParticlesLibrary.fxb` the main particle effects library
- `game:\media\Weather\WeatherManager.fxb` weather system effects

---

## Opening a File

- **Drag and drop** an `.fxb` file onto the page.
- Use the **Open** button in the toolbar.

---

## Page Layout

The page is divided into four linked panels, reflecting the nested hierarchy of the FX Bank format:

```
Bank
Effects  (top-level named effects)
    Phases  (execution phases within an effect)
        Components  (individual FX modules: emitters, forces, etc.)
            Properties  (per-component parameters and values)
```

Selecting an item in any panel populates the next panel to the right with its children, and displays the item's own properties in the detail area at the bottom.

---

## Effects

The **Effects** tree shows all top-level effect entries in the bank. Each effect has:

- A **Name** the string identifier used to play/reference the effect in game code.
- A list of **Phases** that execute in sequence or in parallel when the effect plays.

---

## Phases

Phases are logical execution stages within an effect. Each phase controls timing (when it runs relative to other phases) and contains a set of **Components**.

---

## Components

Components are the individual functional modules inside a phase ï¿½ things like a particle emitter, a force field, a sprite renderer, or a mesh emitter. Each component has a **type** identifier and a list of **Properties** that control its behaviour.

---

## Properties

Each component property has:

| Field | Description |
|---|---|
| **Name** | Human-readable property name from the bank's string table |
| **Type** | Data type of the property value (see table below) |
| **Value** | The stored value, shown in a type-appropriate display format |

### Property Types

| Type | Description |
|---|---|
| `Integer` | A single signed integer |
| `IntegerRange` | A min/max integer pair defining a random range |
| `IntegerArray` | Array of integer values |
| `Float` | A single float |
| `FloatRange` | A min/max float pair |
| `FloatArray` | Array of float values |
| `FloatKeyFrame` | Animated float curve with linear or cubic interpolation |
| `ColorARGBKeyFrame` | Animated ARGB colour curve |
| `String` | A text string (referenced by offset in the string table) |
| `StringArray` | Array of strings |
| `Vector3` | A 3-component float vector (XYZ) |
| `Vector3Range` | A min/max Vector3 pair |
| `Vector3Array` | Array of Vector3 values |
| `Vector4` | A 4-component float vector (XYZW) |
| `FixedFunction` | A procedural curve defined as quadratic or sinusoidal |

---

## Viewing Property Values

Click a property in the Properties list to expand its full detail in the bottom panel. For range types, both the minimum and maximum values are shown. For key-frame types, the curve data is listed.

---

## Limitations

The FXB Viewer is still early development and bugs are likely to occure. 
