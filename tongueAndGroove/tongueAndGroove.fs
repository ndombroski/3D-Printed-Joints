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
            var tongueBody = createTongueBody(context, id + ("tongue" ~ i), lines[i], definition.tongueParts, tongueThicknessWithClearance, tongueLengthWithClearance, {
                "applyChamfer" : definition.chamfer,
                "chamferDistance" : definition.chamfer ? definition.chamferDistance : 0 * millimeter,
                "chamferAngle" : definition.chamfer ? definition.chamferAngle : 0 * degree
            });
            tongueTools = append(tongueTools, tongueBody);
            
            // Create full-size tongue for cutting grooves
            var grooveTool = createTongueBody(context, id + ("groove" ~ i), lines[i], definition.tongueParts, definition.tongueThickness, definition.tongueLength, {
                "applyChamfer" : definition.chamfer,
                "chamferDistance" : definition.chamfer ? definition.chamferDistance : 0 * millimeter,
                "chamferAngle" : definition.chamfer ? definition.chamferAngle : 0 * degree
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
 * @param options : Map containing optional settings:
 *        - applyChamfer : Whether to chamfer the bottom edges
 *        - chamferDistance : Distance for the chamfer
 *        - chamferAngle : Angle for the chamfer
*/
function createTongueBody(context is Context, id is Id, pathLine is Query, tongueParts is Query, tongueThickness is ValueWithUnits, tongueLength is ValueWithUnits, options is map) returns Query
{
    // Get the tangent plane from a face of the tongue parts to determine the extrusion direction
    var facePlane = getFacePlaneForEdge(context, pathLine, tongueParts);
    
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
    
    // Thicken the sheet body sheet body as midpoint
    var thickenId = id + "thicken";
    opThicken(context, thickenId, {
            "entities" : sheetBody,
            "thickness1" : tongueThickness / 2,
            "thickness2" : tongueThickness / 2
    });
    
    // Get the thickened body
    var thickenedBody = qCreatedBy(thickenId, EntityType.BODY);
    

    // Apply chamfer to bottom edges if requested
    if (options.applyChamfer)
    {
        applyChamferToTongueTip(context, id + "chamfer", thickenedBody, endCapEdges, options.chamferDistance, options.chamferAngle);
    }
    
    // Cleanup the sheet body
    opDeleteBodies(context, id + "deleteSheet", {
            "entities" : sheetBody
    });

    // Return the thickened body
    return qCreatedBy(thickenId, EntityType.BODY);
}

/**
 * Between all of the given bodies, find which face the given edge exists on
 * and return a plane tangent to that face. This is used to automatically determine
 * which plane a feature should be created on based off the selected edge and solid, 
 * so that the user doesn't have to explicitly define it.
 * Determines which body the edge is closest to and returns the tangent plane of that face.
 * @param context : The context
 * @param edge : Line/edge to use as reference
 * @param bodies : Bodies to search for the closest face
 * @return Plane : The tangent plane to use for extrusion
*/
function getFacePlaneForEdge(context is Context, edge is Query, bodies is Query) returns Plane
{
    // To determine the plane to use for extruding, find the face of the given bodies
    // that is closest to the first vertex of the given edge.
    var edgeVertices = qAdjacent(edge, AdjacencyType.VERTEX, EntityType.VERTEX);
    var verticesArray = evaluateQuery(context, edgeVertices);
    var vertexPoint = evVertexPoint(context, { "vertex" : verticesArray[0] });
    var closestFaces = qClosestTo(qOwnedByBody(bodies, EntityType.FACE), vertexPoint);
    
    if (isQueryEmpty(context, closestFaces))
    {
        throw regenError("No body found near the selected edge.");
    }
    
    var closestFacesArray = evaluateQuery(context, closestFaces);
    var face = closestFacesArray[0];
    
    // Get the tangent plane from the face to determine the extrusion direction
    return evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : vector(0.5, 0.5)
    });
}

/**
 * applyChamferToTongueTip applies a chamfer to the edges at the tip of a tongue body.
 * The approach for finding the correct edges to chamfer is more complicated because creating the
 * tongue body was two steps: extrude into a sheet body, and then thicken into a solid.
 * First, get the vertices and the end cap of the initial sheet body.
 * Then, find the face of the tongueBody which contains all of these vertices.
 * Then, find all edges of the tongueBody adjacent to this face.
 * @param context : The context
 * @param id : Unique ID for the chamfer operation
 * @param tongueBody : The thickened tongue body
 * @param endCapEdges : Edges from the end cap of the original sheet extrusion
 * @param distance : Distance for the chamfer
 * @param angle : Angle for the chamfer
*/
function applyChamferToTongueTip(context is Context, id is Id, tongueBody is Query, endCapEdges is Query, distance is ValueWithUnits, angle is ValueWithUnits)
{
    // Get vertices from the end cap edges
    var endCapVertices = qAdjacent(endCapEdges, AdjacencyType.VERTEX, EntityType.VERTEX);
    var endCapVertexArray = evaluateQuery(context, endCapVertices);
    
    // Get all faces of the tongue body
    var allTongueFaces = qOwnedByBody(tongueBody, EntityType.FACE);
    var allTongueFacesArray = evaluateQuery(context, allTongueFaces);
    
    // Find face of the tongueBody that contain ALL of the end cap vertex points.
    // Use an array for simplicity, but we expect to only find one (the face furthest)
    // from the tongueBody. 
    var tipFacesList = [];
    for (var face in allTongueFacesArray)
    {
        var containsAllVertices = true;
        for (var vertex in endCapVertexArray)
        {
            var vertexPoint = evVertexPoint(context, { "vertex" : vertex });
            var facesContainingPoint = qContainsPoint(face, vertexPoint);
            if (isQueryEmpty(context, facesContainingPoint))
            {
                containsAllVertices = false;
                break;
            }
        }
        if (containsAllVertices)
        {
            tipFacesList = append(tipFacesList, face);
        }
    }
    
    var tipFaces = qUnion(tipFacesList);

    // Get the edges of the tip faces
    var tipEdges = qAdjacent(tipFaces, AdjacencyType.EDGE, EntityType.EDGE);
    
    // Apply the chamfer to these edges
    opChamfer(context, id, {
            "entities" : tipEdges,
            "chamferType" : ChamferType.OFFSET_ANGLE,
            "width" : distance,
            "angle" : angle
    });
}
