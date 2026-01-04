FeatureScript 2837;
import(path : "onshape/std/common.fs", version : "2837.0");

// This document contains functions for common operations shared between features

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
export function getFacePlaneForEdge(context is Context, edge is Query, bodies is Query) returns Plane
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
 * Between all of the given bodies, find which face the given point is closest to
 * and return a plane tangent to that face. This is used to automatically determine
 * which plane a feature should be created on based off the selected point and solid, 
 * so that the user doesn't have to explicitly define it.
 * Determines which body the point is closest to and returns the tangent plane of that face.
 * @param context : The context
 * @param point : Point/vertex to use as reference
 * @param bodies : Bodies to search for the closest face
 * @return Plane : The tangent plane to use for extrusion
*/
export function getFacePlaneForPoint(context is Context, point is Query, bodies is Query) returns Plane
{
    // To determine the plane to use for extruding, find the face of the given bodies
    // that is closest to the given point.
    var pointLocation = evVertexPoint(context, { "vertex" : point });
    var closestFaces = qClosestTo(qOwnedByBody(bodies, EntityType.FACE), pointLocation);
    
    if (isQueryEmpty(context, closestFaces))
    {
        throw regenError("No body found near the selected point.");
    }
    
    var closestFacesArray = evaluateQuery(context, closestFaces);
    var face = closestFacesArray[0];
    
    // Get the tangent plane from the face to determine the extrusion direction
    return evFaceTangentPlane(context, {
            "face" : face,
            "parameter" : vector(0.5, 0.5)
    });
}