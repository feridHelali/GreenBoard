-- Table des objets géométriques et notes
CREATE TABLE CanvasObjects (
    Id TEXT PRIMARY KEY,
    Type TEXT,          -- 'Rectangle', 'Ellipse', 'Triangle', 'StickyNote'
    X REAL,             -- Position absolue X sur le canvas infini
    Y REAL,             -- Position absolue Y
    Width REAL,
    Height REAL,
    ColorHex TEXT,      -- Sauvegarde de la couleur choisie (ex: #FF0000)
    StrokeThickness REAL
);

-- Table des tracés à main levée (Vectorisés / Points de contrôle)
CREATE TABLE HandDrawnStrokes (
    Id TEXT PRIMARY KEY,
    ColorHex TEXT,
    Thickness REAL,
    GeometryData TEXT   -- Chaîne contenant les points de Bézier ou SVG Path Data (ex: "M 0 0 C 10 10...")
);

