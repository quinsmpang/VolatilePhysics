﻿/*
 *  VolatilePhysics - A 2D Physics Library for Networked Games
 *  Copyright (c) 2015 - Alexander Shoulson - http://ashoulson.com
 *
 *  This software is provided 'as-is', without any express or implied
 *  warranty. In no event will the authors be held liable for any damages
 *  arising from the use of this software.
 *  Permission is granted to anyone to use this software for any purpose,
 *  including commercial applications, and to alter it and redistribute it
 *  freely, subject to the following restrictions:
 *  
 *  1. The origin of this software must not be misrepresented; you must not
 *     claim that you wrote the original software. If you use this software
 *     in a product, an acknowledgment in the product documentation would be
 *     appreciated but is not required.
 *  2. Altered source versions must be plainly marked as such, and must not be
 *     misrepresented as being the original software.
 *  3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;

using UnityEngine;

namespace Volatile
{
  internal static class Collision
  {
    #region Dispatch
    private delegate Manifold CollisionTest(Shape sa, Shape sb);
    private readonly static CollisionTest[,] tests = new CollisionTest[,]
      {
        { __Circle_Circle, __Circle_Polygon },
        { __Polygon_Circle, __Polygon_Polygon}
      };

    internal static Manifold Dispatch(Shape sa, Shape sb)
    {
      CollisionTest test = Collision.tests[(int)sa.Type, (int)sb.Type];
      return test(sa, sb);
    }

    private static Manifold __Circle_Circle(Shape sa, Shape sb)
    {
      return Circle_Circle((Circle)sa, (Circle)sb);
    }

    private static Manifold __Circle_Polygon(Shape sa, Shape sb)
    {
      return Circle_Polygon((Circle)sa, (Polygon)sb);
    }

    private static Manifold __Polygon_Circle(Shape sa, Shape sb)
    {
      return Circle_Polygon((Circle)sb, (Polygon)sa);
    }

    private static Manifold __Polygon_Polygon(Shape sa, Shape sb)
    {
      return Polygon_Polygon((Polygon)sa, (Polygon)sb);
    }
    #endregion

    #region Collision Tests
    private static Manifold Circle_Circle(
      Circle circA,
      Circle circB)
    {
      return TestCircles(circA, circB, circB.cachedWorldCenter, circB.Radius);
    }

    private static Manifold Circle_Polygon(
      Circle circ,
      Polygon poly)
    {
      // Get the axis on the polygon closest to the circle's origin
      float penetration;
      int ix = FindNearestAxis(circ, poly, out penetration);
      if (ix < 0)
        return null;

      Vector2 v =
        poly.cachedWorldVertices[ix];
      Vector2 u =
        poly.cachedWorldVertices[(ix + 1) % poly.cachedWorldAxes.Length];
      Axis a = poly.cachedWorldAxes[ix];

      // If the circle is past one of the two vertices, check it like
      // a circle-circle intersection where the vertex has radius 0
      float d = Util.Cross(a.Normal, circ.cachedWorldCenter);
      if (d > Util.Cross(a.Normal, v))
        return Collision.TestCircles(circ, poly, v, 0.0f);
      if (d < Util.Cross(a.Normal, u))
        return Collision.TestCircles(circ, poly, u, 0.0f);

      // Build the collision Manifold
      // TODO: POOLING
      Manifold manifold = new Manifold(3);
      manifold.shapeA = circ;
      manifold.shapeB = poly;
      Vector2 pos =
        circ.cachedWorldCenter - (circ.Radius + penetration / 2) * a.Normal;
      manifold.UpdateContact(pos, -a.Normal, penetration, 0);
      return manifold;
    }

    private static Manifold Polygon_Polygon(
      Polygon poly1,
      Polygon poly2)
    {
      Axis a1, a2;
      if (Collision.FindMinSepAxis(poly1, poly2, out a1) == false)
        return null;
      if (Collision.FindMinSepAxis(poly2, poly1, out a2) == false)
        return null;

      // We will use poly1's axis, so we may need to swap
      if (a2.Width > a1.Width)
      {
        Util.Swap(ref poly1, ref poly2);
        Util.Swap(ref a1, ref a2);
      }

      // Build the collision Manifold
      // TODO: POOLING
      Manifold manifold = new Manifold(3);
      manifold.shapeA = poly1;
      manifold.shapeB = poly2;
      Collision.FindVerts(poly1, poly2, a1.Normal, a1.Width, manifold);
      return manifold;
    }
    #endregion

    #region Internals
    /// <summary>
    /// Workhorse for circle-circle collisions, compares origin distance
    /// to the sum of the two circles' radii, returns a Manifold.
    /// </summary>
    /// 
    private static Manifold TestCircles(
      Circle shapeA,
      Shape shapeB,
      Vector2 overrideBCenter, // For testing vertices in circles
      float overrideBRadius)
    {
      Vector2 r = overrideBCenter - shapeA.cachedWorldCenter;
      float min = shapeA.Radius + overrideBRadius;
      float distSq = r.sqrMagnitude;

      if (distSq >= min * min)
        return null;

      float dist = Mathf.Sqrt(distSq);
      float distInv = 1.0f / dist;

      Vector2 pos =
        shapeA.cachedWorldCenter +
        (0.5f + distInv * (shapeA.Radius - min / 2.0f)) * r;

      // Build the collision Manifold
      // TODO: POOLING
      Manifold manifold = new Manifold(3);
      manifold.shapeA = shapeA;
      manifold.shapeB = shapeB;
      manifold.UpdateContact(pos, distInv * r, dist - min, 0);
      return manifold;
    }

    /// <summary>
    /// Returns the index of the nearest axis on the poly to the circle.
    /// </summary>
    private static int FindNearestAxis(
      Circle circ,
      Polygon poly,
      out float penetration)
    {
      int ix = 0;
      penetration = float.NegativeInfinity;

      for (int i = 0; i < poly.cachedWorldAxes.Length; i++)
      {
        float dot =
          Vector2.Dot(
            poly.cachedWorldAxes[i].Normal,
            circ.cachedWorldCenter);
        float dist = dot - poly.cachedWorldAxes[i].Width - circ.Radius;

        if (dist > 0)
          return -1;
        if (dist > penetration)
        {
          penetration = dist;
          ix = i;
        }
      }
      return ix;
    }

    private static bool FindMinSepAxis(
      Polygon poly1,
      Polygon poly2,
      out Axis axis)
    {
      axis = new Axis(Vector2.zero, float.NegativeInfinity);
      foreach (Axis a in poly1.cachedWorldAxes)
      {
        float min = float.PositiveInfinity;
        foreach (Vector2 v in poly2.cachedWorldVertices)
          min = Mathf.Min(min, Vector2.Dot(a.Normal, v));
        min -= a.Width;

        if (min > 0)
          return false;
        if (min > axis.Width)
          axis = new Axis(a.Normal, min);
      }

      return true;
    }

    /// <summary>
    /// Add contacts for penetrating vertices. Note that this does not handle
    /// cases where an overlap was detected, but no vertices fall inside the
    /// opposing polygon (like a Star of David). See Chipmunk's cpCollision.c
    /// for more details on how this is resolved.
    /// </summary>
    private static void FindVerts(
      Polygon poly1,
      Polygon poly2,
      Vector2 normal,
      float penetration,
      Manifold manifold)
    {
      uint id = poly1.id << 8;
      foreach (Vector2 v in poly1.cachedWorldVertices)
      {
        if (poly2.ContainsPoint(v))
          if (manifold.UpdateContact(v, normal, penetration, id) == false)
            return;
        id++;
      }

      id = poly2.id << 8;
      foreach (Vector2 v in poly2.cachedWorldVertices)
      {
        if (poly1.ContainsPoint(v))
          if (manifold.UpdateContact(v, normal, penetration, id) == false)
            return;
        id++;
      }
    }
    #endregion
  }
}
