FeatureScript 2837;
import(path : "onshape/std/common.fs", version : "2837.0");

annotation { "Feature Type Name": "Tongue and Groove" }
export const tongueAndGroove = defineFeature(function(context is Context, id is Id, definition is map)
    precondition
    {
        annotation { "Name" : "Path line", "Filter" : EntityType.EDGE, "MaxNumberOfPicks" : 1, "Description" : "Line that the tongue will follow" }
        definition.pathLine is Query;
  
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
        
        annotation { "Name" : "Tongue part", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1, "Description" : "Part that will have a tongue added to it" }
        definition.tonguePart is Query;
        
        annotation { "Name" : "Groove parts", "Filter" : EntityType.BODY, "MaxNumberOfPicks" : 1, "Description" : "Part that will have groove subtracted from it" }
        definition.groovePart is Query;
        
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
        
        // Create tongue body with clearance for adding to tonguePart
        var tongueBody = createTongueBody(context, id + "tongue", definition.pathLine, definition.face, tongueThicknessWithClearance, tongueLengthWithClearance);
        
        // Create full-size tongue for cutting groove
        var grooveTool = createTongueBody(context, id + "groove", definition.pathLine, definition.face, definition.tongueThickness, definition.tongueLength);
        
        // Subtract groove from groovePart
        if (!isQueryEmpty(context, grooveTool)) {            
            opBoolean(context, id + "booleanCut", {
                    "tools" : grooveTool,
                    "targets" : definition.groovePart,
                    "operationType" : BooleanOperationType.SUBTRACTION,
                    "keepTools" : false 
            });
        }
        
        // Join tongue with tonguePart
        if (!isQueryEmpty(context, tongueBody)) {
            opBoolean(context, id + "booleanAdd", {
                "tools" : qUnion([definition.tonguePart, tongueBody]),
                "operationType" : BooleanOperationType.UNION
            });
        }
    });
    
/**
 * createTongueBody creates a tongue body by sweeping a line and thickening it.
 * Returns a Query containing the new body.
 * @param pathLine : Line/edge to serve as the path for the tongue
 * @param face : Face to create the tongue normal to
 * @param tongueThickness : Thickness of the tongue
 * @param tongueLength : Length (extrusion/protrusion depth) of the tongue
*/
function createTongueBody(context is Context, id is Id, pathLine is Query, face is Query, tongueThickness is ValueWithUnits, tongueLength is ValueWithUnits) returns Query
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
    
    // Thicken the sheet body using midplane option - TODO: I don't think this works as midpoint
    var thickenId = id + "thicken";
    opThicken(context, thickenId, {
            "entities" : sheetBody,
            "thickness1" : tongueThickness / 2,
            "thickness2" : tongueThickness / 2
    });

    // Return the thickened body
    return qCreatedBy(thickenId, EntityType.BODY);
}
