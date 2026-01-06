FeatureScript 2837;
import(path : "onshape/std/common.fs", version : "2837.0");
Common::import(path : "91b3d7b1a713c22628315529", version : "7d3303174d0f08b441e2fc90");

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
        
        annotation { "Name" : "Depth clearance", "Description" : "Length to subtract from the tongue to make it shorter than the groove depth" }
        isLength(definition.depthClearance, { (millimeter) : [0, 0, 1e5] } as LengthBoundSpec);
        
        annotation { "Name" : "Tongue parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have tongues added to them" }
        definition.tongueParts is Query;
        
        annotation { "Name" : "Groove parts", "Filter" : EntityType.BODY, "Description" : "Part(s) that will have grooves subtracted from them" }
        definition.grooveParts is Query;
        
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
        var tongueLengthWithClearance = definition.tongueLength - definition.depthClearance;
        
        // Validate that clearances don't make dimensions negative
        if (tongueThicknessWithClearance <= 0 * millimeter)
        {
            throw regenError("Fit clearance is too large. It must be less than the tongue thickness.");
        }
        if (tongueLengthWithClearance <= 0 * millimeter)
        {
            throw regenError("Depth clearance is too large. It must be less than the tongue length.");
        }
        
        // Create arrays to store tongue bodies for boolean operations later
        var tongueTools = []; // For adding to tongueParts (including clearance)
        var grooveTools = []; // For cutting grooves

        // Create tongue bodies for each selected line
        var lines = evaluateQuery(context, definition.pathLines);
        for (var i = 0; i < size(lines); i += 1)
        {
            // Create tongue with clearance for adding to tongueParts
            var tongueBody = createTongueBody(context, id + ("tongue" ~ i), lines[i], definition.tongueParts, tongueThicknessWithClearance, tongueLengthWithClearance, definition.depthClearance, {
                "applyChamfer" : definition.chamfer,
                "chamferDistance" : definition.chamfer ? definition.chamferDistance : 0 * millimeter,
                "chamferAngle" : definition.chamfer ? definition.chamferAngle : 0 * degree,
                "fitClearance" : definition.fitClearance
            });
            tongueTools = append(tongueTools, tongueBody);
            
            // Create full-size tongue for cutting grooves
            var grooveTool = createTongueBody(context, id + ("groove" ~ i), lines[i], definition.tongueParts, definition.tongueThickness, definition.tongueLength, 0 * millimeter, {
                "applyChamfer" : definition.chamfer,
                "chamferDistance" : definition.chamfer ? definition.chamferDistance : 0 * millimeter,
                "chamferAngle" : definition.chamfer ? definition.chamferAngle : 0 * degree,
                "fitClearance" : 0 * millimeter
            });
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
 * createTongueBody creates a tongue body by extruding a path line and thickening it.
 * Returns a Query containing the new body.
 * @param pathLine : Line/edge to serve as the path for the tongue
 * @param tongueParts : Bodies that will have tongues added
 * @param tongueThickness : Thickness of the tongue
 * @param tongueLength : Length (extrusion/protrusion depth) of the tongue
 * @param depthClearance : Depth clearance to subtract from the tongue length
 * @param options : Map containing optional settings:
 *        - applyChamfer : Whether to chamfer the bottom edges
 *        - chamferDistance : Distance for the chamfer
 *        - chamferAngle : Angle for the chamfer
 *        - fitClearance : Fit clearance to apply to side faces
*/
function createTongueBody(context is Context, id is Id, pathLine is Query, tongueParts is Query, tongueThickness is ValueWithUnits, tongueLength is ValueWithUnits, depthClearance is ValueWithUnits, options is map) returns Query
{
    // Get the tangent plane from a face of the tongue parts to determine the extrusion direction
    var facePlane = Common::getFacePlaneForEdge(context, pathLine, tongueParts);
    
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
    
    // Get the end cap edges of the sheet before thickening (these will help us find the tip faces later)
    var endCapEdges = qCapEntity(extrudeId, CapType.END, EntityType.EDGE);
    
    // Get the edges of the sheet body that are NOT cap ends. Will need these
    // are references for adding clearance.
    var allSheetEdges = qOwnedByBody(sheetBody, EntityType.EDGE);
    var startCapEdges = qCapEntity(extrudeId, CapType.START, EntityType.EDGE);
    var allCapEdges = qUnion([endCapEdges, startCapEdges]);
    var nonCapEdges = qSubtraction(allSheetEdges, allCapEdges);
    
    // Thicken the sheet body sheet body as midpoint
    var thickenId = id + "thicken";
    opThicken(context, thickenId, {
            "entities" : sheetBody,
            "thickness1" : tongueThickness / 2,
            "thickness2" : tongueThickness / 2
    });
    
    // Get the thickened body
    var thickenedBody = qCreatedBy(thickenId, EntityType.BODY);
    
    // Add clearance to length of tongue if needed
    if (options.fitClearance != undefined && options.fitClearance > 0 * millimeter)
    {
        addClearanceToLengthOfTongue(context, id + "clearance", thickenedBody, nonCapEdges, facePlane.normal, options.fitClearance);
    }

    // Apply chamfer to bottom edges if requested
    if (options.applyChamfer)
    {
        applyChamferToTongueTip(context, id + "chamfer", thickenedBody, tongueParts, endCapEdges, options.chamferDistance, options.chamferAngle);
    }
    
    // Cleanup the sheet body
    opDeleteBodies(context, id + "deleteSheet", {
            "entities" : sheetBody
    });

    // Return the thickened body
    return qCreatedBy(thickenId, EntityType.BODY);
}

/**
 * addClearanceToLengthOfTongue identifies faces of the thickened body that correspond to the
 * non-cap edges of the original sheet body and extrudes them inward to remove material,
 * creating clearance for the length of the tongue.
 * @param context : The context
 * @param id : Unique ID for operations
 * @param tongueBodyFacesArray : The solid tongue body
 * @param nonCapEdges : Edges from the original sheet body that are not end caps
 * @param extrudeDirection : Normal direction of the original extrusion
 * @param clearance : Amount of clearance to add to the length
*/
function addClearanceToLengthOfTongue(context is Context, id is Id, tongueBody is Query, nonCapEdges is Query, extrudeDirection is Vector, clearance is ValueWithUnits)
{
    // Find faces of tongue body that coincide with nonCapEdges. Those are the faces we need to move inward.
    var nonCapEdgesArray = evaluateQuery(context, nonCapEdges);
    var matchingFacesList = [];
    
    for (var edge in nonCapEdgesArray)
    {
        var matchingFace = Common::faceThatContainsEntireEdge(context, edge, tongueBody);
        if (!isQueryEmpty(context, matchingFace))
        {
            matchingFacesList = append(matchingFacesList, matchingFace);
        }
    }
    
    var facesFromNonCapEdges = qUnion(matchingFacesList);
    
    // Extrude each face inward (opposite to its normal) to remove material
    if (!isQueryEmpty(context, facesFromNonCapEdges))
    {
        var facesArray = evaluateQuery(context, facesFromNonCapEdges);
        var extrudedBodies = [];
        for (var i = 0; i < size(facesArray); i += 1)
        {
            var face = facesArray[i];
            var faceNormal = evFaceTangentPlane(context, {
                    "face" : face,
                    "parameter" : vector(0.5, 0.5)
            }).normal;
            
            var extrudeOpId = id + ("face" ~ i);
            opExtrude(context, extrudeOpId, {
                    "entities" : face,
                    "direction" : -faceNormal,
                    "endBound" : BoundingType.BLIND,
                    "endDepth" : clearance
            });
            
            // Collect the extruded body
            var extrudedBody = qCreatedBy(extrudeOpId, EntityType.BODY);
            extrudedBodies = append(extrudedBodies, extrudedBody);
        }
        
        opBoolean(context, id + "subtract", {
                "tools" : qUnion(extrudedBodies),
                "targets" : tongueBody,
                "operationType" : BooleanOperationType.SUBTRACTION,
                "keepTools" : false
        });
    }
}

/**
 * applyChamferToTongueTip applies a chamfer to the edges at the tip of a tongue body.
 * Finds edges of the tongue body that don't share any points with tongueParts (the tip edges)
 * and applies a chamfer to them.
 * @param context : The context
 * @param id : Unique ID for the chamfer operation
 * @param tongueBody : The thickened tongue body
 * @param tongueParts : The parts that the tongue will be attached to
 * @param endCapEdges : Edges from the end cap of the original sheet extrusion (not used, kept for compatibility)
 * @param distance : Distance for the chamfer
 * @param angle : Angle for the chamfer
*/
function applyChamferToTongueTip(context is Context, id is Id, tongueBody is Query, tongueParts is Query, endCapEdges is Query, distance is ValueWithUnits, angle is ValueWithUnits)
{
    // Get all edges of the tongue body
    var allTongueEdges = qOwnedByBody(tongueBody, EntityType.EDGE);
    var allTongueEdgesArray = evaluateQuery(context, allTongueEdges);
    
    // Filter out edges that share a point with tongueParts
    var tipEdgesList = [];
    for (var edge in allTongueEdgesArray)
    {
        var edgeVertices = qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX);
        var edgeVerticesArray = evaluateQuery(context, edgeVertices);
        var sharesPoint = false;
        
        for (var edgeVertex in edgeVerticesArray)
        {
            var edgeVertexPoint = evVertexPoint(context, { "vertex" : edgeVertex });
            
            if (!isQueryEmpty(context, qContainsPoint(tongueParts, edgeVertexPoint)))
            {
                sharesPoint = true;
                break;
            }
        }
        
        if (!sharesPoint)
        {
            tipEdgesList = append(tipEdgesList, edge);
        }
    }
    
    var tipEdges = qUnion(tipEdgesList);
    
    if (isQueryEmpty(context, tipEdges))
    {
        // Could not find tip edges, skip chamfer
        return;
    }
    
    // Apply the chamfer to these edges
    opChamfer(context, id, {
            "entities" : tipEdges,
            "chamferType" : ChamferType.OFFSET_ANGLE,
            "width" : distance,
            "angle" : angle
    });
}
