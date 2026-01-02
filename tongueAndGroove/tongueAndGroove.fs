FeatureScript 2837;
import(path : "onshape/std/common.fs", version : "2837.0");

annotation { "Feature Type Name": "Tongue and Groove" }
export const tongueAndGroove = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Path lines", "Filter" : EntityType.EDGE, "Description" : "Lines that the tongues will follow" }
        definition.pathLines is Query;
  
        annotation { "Name" : "Tongue thickness", "Description" : "Thickness of the tongue to be created" }
        isLength(definition.tongueThickness, { (millimeter) : [-1e5, 3, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Tongue length", "Description" : "Length of the tongue (how far it is extruded)" }
        isLength(definition.tongueLength, { (millimeter) : [-1e5, 3, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Fit clearance", "Description" : "Dimension to subtract from the thickness of the tongue (only affects tongue dimensions, not the groove's dimensions)" }
        isLength(definition.fitClearance, { (millimeter) : [0, 0, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Length clearance", "Description" : "Length to subtract from the tongue to make it shorter than the groove depth" }
        isLength(definition.lengthClearance, { (millimeter) : [0, 0, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Face", "Filter" : EntityType.FACE, "MaxNumberOfPicks" : 1, "Description" : "Face to create the tongue normal to. The tongue will protrude outward from the body of the selected face" }
        definition.face is Query;
        
        annotation { "Name" : "Tongue parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have tongues added to them" }
        definition.tongueParts is Query;
        
        annotation { "Name" : "Groove parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have grooves subtracted from them" }
        definition.grooveParts is Query;
        
        annotation { "Name" : "Add draft angle to tongue", "Description": "Add slight inward angle along the length of the tongue" }
        definition.addDraftAngle is boolean;
        
        if (definition.addDraftAngle)
        {
            annotation { "Name" : "Draft angle" }
            isAngle(definition.draftAngle, { (degree) : [0, 10, 90] } as AngleBoundSpec);
        }
        
        annotation { "Name" : "Add chamfer to tongue", "Description": "Add chamfer to the edges of the tongue that are inserted into the groove" }
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
        var tongueThicknessWithClearance = definition.tongueThickness - definition.fitClearance;
        var tongueLengthWithClearance = definition.tongueLength - definition.lengthClearance;
        
        // Validate that clearances don't make dimensions negative
        if (tongueThicknessWithClearance <= 0 * millimeter)
        {
            throw regenError("Fit clearance is too large. It must be less than the tongue thickness.");
        }
        if (tongueLengthWithClearance <= 0 * millimeter)
        {
            throw regenError("Length clearance is too large. It must be less than the tongue length.");
        }
        
        // Create arrays to store tongue bodies for boolean operations later
        var tongueTools = []; // For adding to tongueParts (including clearance)
        var grooveTools = []; // For cutting grooves

        // Create tongue bodies for each selected line
        var lines = evaluateQuery(context, definition.pathLines);
        for (var i = 0; i < size(lines); i += 1)
        {
            // Create tongue with clearance for adding to tongueParts
            var tongueBody = createTongueBody(context, id + ("tongue" ~ i), lines[i], definition.face, tongueThicknessWithClearance, tongueLengthWithClearance, definition.addDraftAngle, definition.addDraftAngle ? definition.draftAngle : 0 * degree, definition.chamfer, definition.chamfer ? definition.chamferDistance : 0 * millimeter, definition.chamfer ? definition.chamferAngle : 0 * degree);
            tongueTools = append(tongueTools, tongueBody);
            
            // Create full-size tongue for cutting grooves
            var grooveTool = createTongueBody(context, id + ("groove" ~ i), lines[i], definition.face, definition.tongueThickness, definition.tongueLength, definition.addDraftAngle, definition.addDraftAngle ? definition.draftAngle : 0 * degree, definition.chamfer, definition.chamfer ? definition.chamferDistance : 0 * millimeter, definition.chamfer ? definition.chamferAngle : 0 * degree);
            grooveTools = append(grooveTools, grooveTool);
        }
        
        // Perform boolean operations to cut the grooves and add the tongues respectively
        
        // Create queries that encompass all tongue bodies
        var allTongueTools = qUnion(tongueTools);
        var allGrooveTools = qUnion(grooveTools);
        
        // Subtract bodies from grooveParts
        if (!isQueryEmpty(context, allGrooveTools)) {            
            opBoolean(context, id + "booleanCut", {
                    "tools" : allGrooveTools,
                    "targets" : definition.grooveParts,
                    "operationType" : BooleanOperationType.SUBTRACTION,
                    "keepTools" : false 
            });
        }
        
        // Join bodies with tongueParts
        if (!isQueryEmpty(context, allTongueTools)) {
            // Must perform boolean on each individual tonguePart to avoid joining the tongue parts themselves to each other.
            var tongueParts = evaluateQuery(context, definition.tongueParts);
            for (var i = 0; i < size(tongueParts); i += 1)
            {
                opBoolean(context, id + ("booleanAdd" ~ i), {
                    "tools" : qUnion([tongueParts[i], allTongueTools]),
                    "operationType" : BooleanOperationType.UNION
                });
            }
        }
    });
    
/**
 * createTongueBody creates a tongue body by sweeping a line and thickening it.
 * Returns a Query containing the new body.
 * @param pathLine : Line/edge to serve as the path for the tongue
 * @param face : Face to create the tongue normal to
 * @param tongueThickness : Thickness of the tongue
 * @param tongueLength : Length (extrusion/protrusion depth) of the tongue
 * @param applyDraftAngle : Whether to apply a draft angle
 * @param draftAngle : Angle for the draft
 * @param applyChamfer : Whether to chamfer the bottom edges
 * @param chamferDistance : Distance for the chamfer
 * @param chamferAngle : Angle for the chamfer
*/
function createTongueBody(context is Context, id is Id, pathLine is Query, face is Query, tongueThickness is ValueWithUnits, tongueLength is ValueWithUnits, applyDraftAngle is boolean, draftAngle is ValueWithUnits, applyChamfer is boolean, chamferDistance is ValueWithUnits, chamferAngle is ValueWithUnits) returns Query
{
    // Get the tangent plane from the face to determine the extrusion direction
    var facePlane = evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : vector(0.5, 0.5)
    });
    
    // Extrude the line into a sheet body along the face normal
    var extrudeId = id + "extrude";
    opExtrude(context, extrudeId, {
            "entities" : pathLine,
            "direction" : facePlane.normal,
            "endBound" : BoundingType.BLIND,
            "endDepth" : tongueLength
    });
    
    // Get the created sheet body
    var sheetBody = qCreatedBy(extrudeId, EntityType.BODY);
    
    // Thicken the sheet body sheet body as midpoint
    var thickenId = id + "thicken";
    opThicken(context, thickenId, {
            "entities" : sheetBody,
            "thickness1" : tongueThickness / 2,
            "thickness2" : tongueThickness / 2
    });
    
    // Apply draft angle if requested
    if (applyDraftAngle)
    {
        applyChamferToEndCap(context, id + "draftChamfer", thickenId, tongueLength, draftAngle);
    }
    
    // Apply chamfer to bottom edges if requested
    if (applyChamfer)
    {
        applyChamferToEndCap(context, id + "chamfer", thickenId, chamferDistance, chamferAngle);
    }
    
    // Cleanup the sheet body
    opDeleteBodies(context, id + "deleteSheet", {
            "entities" : sheetBody
    });

    // Return the thickened body
    return qCreatedBy(thickenId, EntityType.BODY);
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
    var endCapFace = qCapEntity(extrudeId, CapType.EITHER, EntityType.FACE);
    
    var endCapFaceResolved = evaluateQuery(context, endCapFace);
    println("endCapFaceResolved");
    println(endCapFaceResolved);
    
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
