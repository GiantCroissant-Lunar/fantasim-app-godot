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


---

# v3 — plate shapes, not puzzle shapes (second correction)

v2's generations were structurally right (thickness, strata walls, molten core, closed-vs-
offset states) but literalized "jigsaw": tab-and-socket piece shapes, near-uniform sizes.
User: "we are not making jigsaw puzzle, this is just a term. actually each piece should be
[the Sketchfab model] which is not regular and not jigsaw puzzle shape, some plates are
bigger, some plates are smaller."

Changes: noun is "tectonic plates"; shapes are "irregular angular fragments with jagged,
zigzagging edges"; explicit size hierarchy (quarter-sphere plates down to narrow slivers);
jigsaw tabs/knobs/uniform sizes banned in negatives; water/trees negatives strengthened.

## Prompt A v3 — ASSEMBLED

A stylized planet whose entire rocky shell is divided into tectonic plates: large irregular
angular fragments with jagged, zigzagging edges, like the broken shell of an egg — never
rounded puzzle shapes. The pieces vary greatly in size: two or three huge plates each spanning
a quarter of the sphere, several medium plates, and a few small narrow slivers wedged between
them. The plates fit together perfectly into one complete closed sphere — no gaps, nothing
beneath them visible — but every boundary is clearly visible as a thin engraved seam line,
faintly glowing ember-red, tracing jagged irregular outlines across the whole surface. Each
plate carries chunky stylized dry rocky terrain: faceted mountains, plateaus, boulder fields,
giving the planet a gently knobbly silhouette. Matte clay game-art render, cartoon shader,
soft key light, dark space background, square format.

Negative: jigsaw tabs, puzzle knobs, interlocking sockets, rounded puzzle pieces, uniform
piece sizes, hexagonal grid, open gaps, cracks with lava rivers, exploded pieces, visible
interior, water, rivers, lakes, trees, forests, buildings, clouds, text

## Prompt B v3 — EXPLODED

A stylized planet in exploded view: its rocky shell is divided into tectonic plates — large
irregular angular fragments with jagged, zigzagging edges, never rounded puzzle shapes, their
sizes varying greatly from huge quarter-sphere plates down to small narrow slivers. Every
plate is lifted straight outward from the planet's center by a small equal distance, so the
pieces no longer touch and no longer form a complete sphere — open space separates each plate
from its neighbors. Each floating plate is a thick slab: jagged carved side walls showing
layered rock strata, a cut underside, and chunky faceted dry rocky terrain riding on its top
surface. Through the gaps between plates, the planet's interior is revealed: a smaller glowing
molten orange core sphere at the center beneath the separated shell pieces. Matte clay
game-art render, cartoon shader, soft key light, dark space background, square format.

Negative: jigsaw tabs, puzzle knobs, rounded puzzle pieces, uniform piece sizes, complete
closed sphere, touching pieces, water, rivers, lakes, trees, forests, buildings, clouds,
text, blur


---

# v4 — exploded view carries the BOUNDARY RELATIONSHIPS (third correction; Vigil grammar)

v3's exploded generation read as unrelated floating rocks: every edge a plain vertical cut.
User: "each piece(plate) should have some parts related to other piece(plate) like how [the
USGS Vigil cross-section] shows. So we can see plate is under, above another one."
Assembled Prompt A v3 stands unchanged; only the exploded prompt revises.

## Prompt B v4 — EXPLODED with interaction-shaped edges

A stylized planet in exploded view: its rocky shell divided into tectonic plates — large
irregular angular fragments, sizes varying greatly from huge quarter-sphere plates down to
small slivers — each lifted outward from the planet's center so open space separates the
pieces, revealing a glowing molten orange core sphere at the center. Each plate is a thick
slab: layered rock strata on its carved side walls, chunky faceted dry rocky terrain on top.
Most importantly, the plates' edges keep the shapes of their interactions, like a 3D geology
textbook cross-section: where two plates collide, one plate's edge bends DOWNWARD like a
diving tongue, reaching in beneath the neighboring plate's edge, which is raised and thickened
into a mountain ridge riding ABOVE the diving tongue — so even with the pieces pulled apart
you can clearly see which plate goes under and which rides over. Facing edges of neighboring
plates mirror each other's jagged outlines, so the pieces obviously belong together. At a
spreading boundary, the two facing edges are thin and freshly formed, angled down toward the
glowing interior. Matte clay game-art render, cartoon shader, soft key light, dark space
background, square format.

Negative: independent floating asteroid chunks, plain straight vertical cut edges, unrelated
rocks, jigsaw tabs, puzzle shapes, uniform piece sizes, complete closed sphere, touching
pieces, water, rivers, trees, forests, buildings, clouds, text, blur

Load-bearing phrases: "edges keep the shapes of their interactions" (kills plain-cut
asteroids); "diving tongue beneath / mountain ridge riding above" (Vigil subduction pair,
readable while separated); "facing edges mirror each other's jagged outlines" (pieces
provably one sphere).


---

# v5 — overlap IS the relationship (fourth correction)

v4's generation still produced unrelated rocks. Root cause identified in the PROMPT LANGUAGE:
"lifted outward by an equal distance" — uniform radial explosion destroys relationships by
construction. User: "Each piece should be related to other piece, but the image shows they
have no part interact with another."

The relationship = OVERLAP: at every boundary one plate's edge extends UNDERNEATH the
neighbor's raised edge; the separation opens a thin lit gap BETWEEN the overlapping surfaces
(roof shingles pulled slightly apart — not rocks flung apart).

## Prompt B v5 — EXPLODED as overlapping shingles

A stylized planet gently pulled apart into seven thick tectonic plates, only slightly
separated — the pieces stay close together, still clearly forming a sphere, with a glowing
molten orange core showing through the narrow gaps between them. The defining feature: at
every boundary, neighboring plates OVERLAP like roof shingles — the edge of one plate extends
inward and slides UNDERNEATH the raised edge of its neighbor, leaving a thin glowing gap
between the lower plate's top surface and the upper plate's underside, so it is obvious which
plate lies under and which rides above. In the foreground, one overlap is prominent: a lower
plate's long edge reaching deep beneath its neighbor's thick overhanging lip, that lip
crumpled upward into a mountain ridge. Each plate is a thick slab of layered rock strata with
chunky faceted dry rocky terrain on top; their outlines are irregular and jagged, their sizes
all different. Matte clay game-art render, cartoon shader, soft key light, dark space
background, square format.

Negative: rocks scattered far apart, isolated floating islands, equal uniform explosion, no
overlapping edges, plain vertical cut edges, jigsaw shapes, symmetrical arrangement, water,
rivers, trees, buildings, clouds, text, blur

NOTE (standing): if v5 also misses, stop iterating words — the app's renderer becomes the
reference-maker: implement interaction-preserving edges in the exploded path and screenshot
THAT. Only the app knows the true plate relationships.
