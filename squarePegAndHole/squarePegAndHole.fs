FeatureScript 2837;
import(path : "onshape/std/common.fs", version : "2837.0");

annotation { "Feature Type Name": "Square Peg and Hole" }
export const pegAndHole = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Peg center points", "Filter" : EntityType.VERTEX, "Description" : "Center points of the pegs to be created" }
        definition.points is Query;
        
        annotation { "Name" : "Peg width", "Description" : "Width of the square pegs to be created" }
        isLength(definition.pegWidth, { (millimeter) : [-1e5, 3, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Peg height", "Description" : "Height of the square pegs to be created" }
        isLength(definition.pegHeight, { (millimeter) : [-1e5, 3, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Peg length", "Description" : "Length of the square pegs (how far it is extruded)" }
        isLength(definition.pegLength, { (millimeter) : [-1e5, 3, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Fit clearance", "Description" : "Length to subtract from the width and height of the peg (only affects peg dimensions, not the hole's dimensions)" }
        isLength(definition.fitClearance, { (millimeter) : [0, 0, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Length clearance", "Description" : "Length to subtract from the peg length to make it shorter than the hole depth" }
        isLength(definition.lengthClearance, { (millimeter) : [0, 0, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Rotation Angle", "Description" : "Rotation of the square pegs around the center point." }
        isAngle(definition.rotationAngle, { (degree) : [-360, 45, 360] } as AngleBoundSpec);
        
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1, "Description" : "Face to create the pegs normal to" }
        definition.face is Query;
        
        annotation { "Name" : "Peg parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have pegs added to them" }
        definition.pegParts is Query;
        
        annotation { "Name" : "Hole parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have holes subtracted from them" }
        definition.holeParts is Query;
        
        annotation { "Name" : "Add draft angle to peg" }
        definition.addDraftAngle is boolean;
        
        if (definition.addDraftAngle)
        {
            annotation { "Name" : "Draft angle" }
            isAngle(definition.draftAngle, { (degree) : [0, 10, 90] } as AngleBoundSpec);
        }
        
        annotation { "Name" : "Add chamfer to peg" }
        definition.chamfer is boolean;
        
        if (definition.chamfer)
        {
            annotation { "Name" : "Chamfer distance" }
            isLength(definition.chamferDistance, { (millimeter) : [0, 0.5, 1e5] } as LengthBoundSpec);
            
            annotation { "Name" : "Chamfer angle" }
            isAngle(definition.chamferAngle, { (degree) : [0, 45, 90] } as AngleBoundSpec);
        }
        
    }
    {        
        // Calculate effective dimensions with clearance applied
        var pegWidthWithClearance = definition.pegWidth - definition.fitClearance;
        var pegHeightWithClearance = definition.pegHeight - definition.fitClearance;
        var pegLengthWithClearance = definition.pegLength - definition.lengthClearance;
        
        // Validate that clearances don't make dimensions negative
        if (pegWidthWithClearance <= 0 * millimeter || pegHeightWithClearance <= 0 * millimeter)
        {
            throw regenError("Fit clearance is too large. It must be less than both the peg width and peg height.");
        }
        if (pegLengthWithClearance <= 0 * millimeter)
        {
            throw regenError("Length clearance is too large. It must be less than the peg length.");
        }
        
        // Create arrays to store peg bodies for boolean operations later
        var pegTools = []; // For adding to pegParts (including clearance)
        var holeTools = []; // For cutting holes

        // Create peg bodies for each selected point
        var points = evaluateQuery(context, definition.points);
        for (var i = 0; i < size(points); i += 1)
        {
            // Create peg with clearance for adding to pegParts
            var pegBody = createPegBody(context, id + ("peg" ~ i), points[i], definition.face, pegWidthWithClearance, pegHeightWithClearance, pegLengthWithClearance, definition.rotationAngle, definition.addDraftAngle, definition.addDraftAngle ? definition.draftAngle : 0 * degree, definition.chamfer, definition.chamfer ? definition.chamferDistance : 0 * millimeter, definition.chamfer ? definition.chamferAngle : 0 * degree);
            pegTools = append(pegTools, pegBody);
            
            // Create full-size peg for cutting holes
            var holeTool = createPegBody(context, id + ("hole" ~ i), points[i], definition.face, definition.pegWidth, definition.pegHeight, definition.pegLength, definition.rotationAngle, definition.addDraftAngle, definition.addDraftAngle ? definition.draftAngle : 0 * degree, definition.chamfer, definition.chamfer ? definition.chamferDistance : 0 * millimeter, definition.chamfer ? definition.chamferAngle : 0 * degree);
            holeTools = append(holeTools, holeTool);
        }
        
        // Perform boolean operations to cut the holes and add the pegs respectively
        
        // Create queries that encompass all peg bodies
        var allPegTools = qUnion(pegTools);
        var allHoleTools = qUnion(holeTools);
        
        // Subtract bodies to holeParts
        if (!isQueryEmpty(context, allHoleTools)) {            
            opBoolean(context, id + "booleanCut", {
                    "tools" : allHoleTools,
                    "targets" : definition.holeParts,
                    "operationType" : BooleanOperationType.SUBTRACTION,
                    "keepTools" : false 
            });
        }
        
        // Join bodies with pegParts.        
        if (!isQueryEmpty(context, allPegTools)) {
            // Must perform boolean on each individual pegPart to avoid joining the peg parts themselves to each other.
            var pegParts = evaluateQuery(context, definition.pegParts);
            for (var i = 0; i < size(pegParts); i += 1)
            {
                opBoolean(context, id + ("booleanAdd" ~ i), {
                    "tools" : qUnion([pegParts[i], allPegTools]),
                    "operationType" : BooleanOperationType.UNION
                });
            }
        }
    });
    
/**
 * createPegBody creates a single peg body at a specific point.
 * Returns a Query containing the new body.
 * @param point : Point to serve as midpoint of the peg
 * @param face : Face to create the peg normal to
 * @param pegWidth : Width of the rectangular peg
 * @param pegHeight : Height of the rectangular peg
 * @param pegLength : Length (extrusion depth) of the peg
 * @param rotationAngle : Rotation angle of the peg around the center point
 * @param applyDraftAngle : Whether to apply a draft angle
 * @param draftAngle : Angle for the draft
 * @param applyChamfer : Whether to chamfer the bottom edges
 * @param chamferDistance : Distance for the chamfer
 * @param chamferAngle : Angle for the chamfer
*/
function createPegBody(context is Context, id is Id, point is Query, face is Query, pegWidth is ValueWithUnits, pegHeight is ValueWithUnits, pegLength is ValueWithUnits, rotationAngle is ValueWithUnits, applyDraftAngle is boolean, draftAngle is ValueWithUnits, applyChamfer is boolean, chamferDistance is ValueWithUnits, chamferAngle is ValueWithUnits) returns Query
{
    // Get the 3D coordinates of the selected point
    var worldPoint = evVertexPoint(context, {
            "vertex" : point
    });

    // Create plane normal to the selected face
    var sketchPlane = evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : vector(pegWidth, pegWidth) 
    });
        
    // Adjust the plane origin to be at our selected point
    sketchPlane.origin = worldPoint;
        
    // Rotate plane according to input
    var rotation = rotationMatrix3d(sketchPlane.normal, rotationAngle);
    sketchPlane.x = rotation * sketchPlane.x;

    // Create the sketch
    var sketchId = id + "sketch";
    var sketch = newSketchOnPlane(context, sketchId, {
            "sketchPlane" : sketchPlane
    });

    // Draw the rectangle centered at the point
    skRectangle(sketch, "square", {
            "firstCorner" : vector(pegWidth * -0.5, pegHeight * -0.5),
            "secondCorner" : vector(pegWidth * 0.5, pegHeight * 0.5)
    });

    // Solve the sketch
    skSolve(sketch);

    // Extrude to create the peg body
    var extrudeId = id + "extrude";
    opExtrude(context, extrudeId, {
            "entities" : qSketchRegion(sketchId),
            "direction" : sketchPlane.normal,
            "endBound" : BoundingType.BLIND,
            "endDepth" : pegLength
    });
    
    // Apply draft angle if requested
    if (applyDraftAngle)
    {
        applyChamferToEndCap(context, id + "draftChamfer", extrudeId, pegLength, draftAngle);
    }
    
    // Apply chamfer to bottom edges if requested
    if (applyChamfer)
    {
        applyChamferToEndCap(context, id + "chamfer", extrudeId, chamferDistance, chamferAngle);
    }
    
    // Cleanup the sketch
    opDeleteBodies(context, id + "deleteSketch", {
            "entities" : qCreatedBy(sketchId, EntityType.BODY)
    });

    // Return the created body
    return qCreatedBy(extrudeId, EntityType.BODY);
}

/**
 * applyChamferToEndCap applies a chamfer to the edges of the end cap face of an extrusion.
 * @param context : The context
 * @param id : Unique ID for the chamfer operation
 * @param extrudeId : ID of the extrude operation
 * @param distance : Distance for the chamfer
 * @param angle : Angle for the chamfer
*/
function applyChamferToEndCap(context is Context, id is Id, extrudeId is Id, distance is ValueWithUnits, angle is ValueWithUnits)
{
    // Get the end cap face of the extrusion (opposite to the sketch plane)
    var endCapFace = qCapEntity(extrudeId, CapType.END, EntityType.FACE);
    
    // Get the edges of the end cap face
    var bottomEdges = qAdjacent(endCapFace, AdjacencyType.EDGE, EntityType.EDGE);
    
    // Apply the chamfer
    opChamfer(context, id, {
            "entities" : bottomEdges,
            "chamferType" : ChamferType.OFFSET_ANGLE,
            "width" : distance,
            "angle" : angle
    });
}
