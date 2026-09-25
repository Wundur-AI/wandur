# Armored surface wear

`armored-wear.png` is an original generated transparent PNG, created September 25,
2026 with the built-in image-generation tool. It is not extracted from the supplied
reference. The delivered image is 1254 square pixels, about 76 KiB. No image edits
were performed after generation.

`ArmoredWear` uses only its alpha channel. One cached mask is tiled at 384 DIPs and
composited as a faint dark scratch with an offset light lip. Its RGB never supplies
the metal color. The same tile therefore works with light/dark/custom palettes.
It is clipped to drawn metal plates, excluded from text wells and controls, and
never stretched with the window. A second image is unnecessary for the paired
light/dark treatment. Tests check transparent boundaries, sparse coverage,
repeat alignment, resize stability and clipping.

## Generation prompt

Generate a production texture asset, not a mockup. Square 1024x1024 PNG with
GENUINELY TRANSPARENT background and alpha channel. A very sparse seamless tile of
fine short hairline scratches and tiny abrasion flecks for a clean machined
spacecraft UI metal surface. Scratch marks neutral charcoal gray only, no colored
pigments, no metal substrate, no opaque background, no lighting, no frame, no
shadows, no text, no logos. At least 95 percent of the image is transparent empty
space. Each individual mark is small and thin: most 4-20 pixels long at native
resolution, occasional 30px, roughly 1px hairlines. Mostly horizontal with a few
diagonal fine scuffs, distributed evenly but irregularly, no big clusters, no
recognizable repeated motif, no long strokes. Keep outermost 12px entirely
transparent so repeating the PNG introduces no cut-off strokes; maintain similarly
sparse density just inside edges. This is subtle clean use wear, not grunge,
cracking, rust, chipped paint, stains, noise or dirt. Deliver the alpha PNG asset.
