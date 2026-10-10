#ifndef NURBSSCENE_KERNEL_H
#define NURBSSCENE_KERNEL_H

/*
 * NurbsScene numerical kernel, ABI 1. All scalars are IEEE-754 doubles.
 * Callers own memory; operations neither allocate nor retain pointers.
 * Control points are homogeneous (x*w,y*w,z*w,w), strictly positive weights.
 * Expanded knot count is control_count + degree + 1. Surface control points
 * are row-major, V varying fastest. Input/output ranges must not overlap.
 * Evaluation is right-sided at internal knots and left-sided at the end.
 * Derivatives are with respect to the supplied (not normalized) parameters.
 */
#if defined(_WIN32)
# define NS_API __declspec(dllexport)
#else
# define NS_API __attribute__((visibility("default")))
#endif
#ifdef __cplusplus
extern "C" {
#endif

typedef enum ns_status {
 NS_OK = 0, NS_ARGUMENT = 1, NS_KNOTS = 2, NS_WEIGHTS = 3,
 NS_DOMAIN = 4, NS_WORKSPACE = 5, NS_NUMERICAL = 6,
 NS_MULTIPLICITY = 7, NS_UNSUPPORTED = 8, NS_CAPACITY = 9
} ns_status;

NS_API int ns_abi_version(void);
NS_API int ns_curve_workspace(int degree, int derivative_order);
NS_API int ns_surface_workspace(int degree_u, int degree_v, int order_u, int order_v);
NS_API int ns_validate_curve(int degree, int count, const double *knots,
 const double *homogeneous);
NS_API int ns_find_span(int degree, int count, const double *knots, double parameter);
NS_API int ns_basis(int degree, int count, const double *knots, double parameter,
 int derivative_order, double *derivatives, int derivative_capacity,
 double *workspace, int workspace_capacity);
NS_API int ns_curve_eval(int degree, int count, const double *knots,
 const double *homogeneous, double parameter, int derivative_order,
 double *result_xyz, int result_capacity, double *workspace, int workspace_capacity);
NS_API int ns_surface_eval(int degree_u, int degree_v, int count_u, int count_v,
 const double *knots_u, const double *knots_v, const double *homogeneous,
 double parameter_u, double parameter_v, int order_u, int order_v,
 double *result_xyz, int result_capacity, double *workspace, int workspace_capacity);
/* Batched evaluation validates geometry once, then shares basis workspace. */
NS_API int ns_curve_samples(int degree,int count,const double *knots,const double *homogeneous,
 const double *parameters,int samples,int order,double *out,int capacity,double *workspace,int workspace_capacity);
NS_API int ns_surface_samples(int degree_u,int degree_v,int count_u,int count_v,
 const double *knots_u,const double *knots_v,const double *homogeneous,
 const double *parameters_uv,int samples,int order_u,int order_v,double *out,int capacity,
 double *workspace,int workspace_capacity);
/* Inserts an interior knot once. Output has count+1 control points. */
NS_API int ns_curve_insert(int degree, int count, const double *knots,
 const double *homogeneous, double parameter, double *output_knots,
 int output_knot_capacity, double *output_homogeneous, int output_point_capacity);
/* Homogeneous de Casteljau subdivision of one Bezier segment. */
NS_API int ns_bezier_split(int degree, const double *homogeneous, double fraction,
 double *left, double *right, double *workspace, int workspace_capacity);
/* Exact homogeneous degree elevation of a Bezier segment by one. */
NS_API int ns_bezier_elevate(int degree, const double *homogeneous,
 double *elevated, int output_capacity);
/* In-place, scaled partial-pivot LU solve; b contains rhs_count columns. */
NS_API int ns_solve_linear(int size, int rhs_count, double *matrix, double *rhs,
 double *workspace, int workspace_capacity);
NS_API double ns_distance_point_segment(double px, double py, double pz,
 double ax, double ay, double az, double bx, double by, double bz);
#ifdef __cplusplus
}
#endif
#endif
