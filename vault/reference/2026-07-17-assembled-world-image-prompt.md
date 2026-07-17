# Assembled-world acceptance image — generation prompt (2026-07-17)

Written by the lead agent at the user's request as the "prove you understand" synthesis of
the registry references; the user will generate the image with an image-AI service and the
picked result becomes THE acceptance reference (`2026-07-17-user-reference-assembled-world.*`).

Fuses: Sketchfab exploded-plates (split thick plates, glowing joints, underride) + the
cartoon planet (knobbly silhouette, cartoon-clay finish) + the gray geometry ball (faceted
exaggerated bulk) + the 06-20 doctrine (two crust materials riding one plate) + the
waterless / no-biome locks.

## Main prompt (assembled, joints visible)

A single stylized planet floating in dark empty space, three-quarter view, rendered like
modern game art with a cartoon shader and matte clay materials. The planet is assembled from
about ten discrete tectonic plates, like a thick-walled ceramic sphere broken into large
irregular pieces and fitted back together with narrow open gaps between every piece. Each
plate is a visibly THICK slab — wherever two plates meet, their cut side-walls are exposed,
showing layered rock strata — and the gaps glow from within: incandescent orange-red molten
rock shining between the plates, yellow-white hot where plates collide, dim red where they
quietly slide past. At one collision boundary, one plate visibly slides BENEATH its neighbor:
the overriding plate's edge is crumpled upward into a chunky mountain ridge, with a deep
trench groove running along the dive line. At another boundary two plates pull apart, the
widening gap floored with fresh dark rock. The crust riding on top of each plate is chunky,
low-poly, faceted terrain with exaggerated bulk — lumpy mountains and plateaus that break the
planet's silhouette, so its outline is knobbly, never a smooth circle. Two materials share
each plate: thick pale continental rafts standing high, and lower darker rocky plains around
them. A dry rocky world with hypsometric color banding — dark lowlands through warm tan to
pale peaks. Crisp faceted shading, soft key light, subtle rim light, square format, high
detail.

## Negative prompt

photorealistic Earth, oceans, water, rivers, clouds, atmosphere haze, vegetation, forests,
ice caps, city lights, text, labels, UI, smooth seamless sphere, hairline cracks, perfectly
circular silhouette, blur, fur, noise

## Exploded sibling (append for the second reference)

Same planet with every plate lifted radially outward a small distance, exploded-view style,
revealing the glowing molten interior sphere beneath the separated thick slabs.


---

# v2 — CORRECTED after the first two generations missed (user's jigsaw formulation)

User verbatim: "Think this is a 3d jigsaw puzzle of the earth, where each piece has thickness.
One view is each pieces form a complete planet, but we could not see what is under plate. The
other view is each piece is placing outward a bit as [the Sketchfab model] shows, so we can see
what is under plate, because they don't form a complete sphere, instead each piece has
offset(space) to another piece."

v1's failure: it mixed the states — open glowing gaps on the ASSEMBLED planet → both models
drew a cracked lava ball. The two states differ ONLY by the radial offset (exactly the app's
`render.exploded` factor 0 vs > 0).

## Prompt A — ASSEMBLED (closed sphere, seams as lines, nothing under visible)

A stylized 3D jigsaw puzzle globe: a complete spherical planet assembled from about twelve
thick interlocking puzzle pieces of rock. The pieces fit together perfectly — no gaps, no
cracks, no missing pieces, one whole closed sphere — but every seam between pieces is clearly
visible as a thin engraved boundary line, faintly glowing ember-red, tracing irregular
interlocking outlines across the whole surface. Each piece carries chunky stylized terrain on
top: faceted mountains, plateaus, dry rocky plains, making the planet's silhouette gently
knobbly. The sphere is completely closed — nothing under the pieces is visible. Matte clay
game-art render, cartoon shader, soft key light, dark space background, square format.

Negative: cracks, open gaps, lava rivers between pieces, exploded pieces, floating fragments,
missing piece, visible interior, hollow, water, clouds, vegetation, text

## Prompt B — EXPLODED (uniform outward offset, thickness + molten interior visible)

A stylized 3D jigsaw puzzle globe in exploded view: the same twelve thick rock puzzle pieces,
each one lifted straight outward from the planet's center by a small equal distance, so the
pieces no longer touch and no longer form a complete sphere — clear open space separates every
piece from its neighbors. Each floating piece shows its full thickness: carved side walls with
layered rock strata and a flat cut underside, with chunky faceted terrain riding on its top
surface. Through the open spaces between the pieces, the planet's interior is revealed: a
smaller glowing molten orange core sphere at the center, beneath the separated shell pieces.
Matte clay game-art render, cartoon shader, soft key light, dark space background, square format.

Negative: complete closed sphere, touching pieces, single cracked ball, seams only, water,
clouds, vegetation, text, blur

Optional single-generation variant for piece-consistency: "two views of the same object side
by side: left fully assembled, right exploded outward".
